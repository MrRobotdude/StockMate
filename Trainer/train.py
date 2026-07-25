from __future__ import annotations

import argparse
import concurrent.futures
import hashlib
import itertools
import json
from dataclasses import dataclass, asdict
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
import pandas as pd


@dataclass(frozen=True)
class Params:
    buy_score: int
    watch_score: int
    volume_confirmation: float
    atr_stop_multiplier: float
    minimum_risk_reward: float


GRID = [
    Params(*values)
    for values in itertools.product(
        (72, 76, 80, 84),
        (60, 64, 68),
        (1.10, 1.25, 1.40),
        (1.20, 1.35, 1.55),
        (1.8, 2.0, 2.4),
    )
    if values[1] < values[0]
]


def indicators(frame: pd.DataFrame) -> pd.DataFrame:
    d = frame.sort_values("Date").copy()
    close = d["Close"].astype(float)
    d["SMA20"] = close.rolling(20).mean()
    d["SMA50"] = close.rolling(50).mean()
    d["AVG_VOLUME20"] = d["Volume"].astype(float).rolling(20).mean()
    delta = close.diff()
    gain = delta.clip(lower=0).rolling(14).mean()
    loss = (-delta.clip(upper=0)).rolling(14).mean()
    d["RSI"] = 100 - 100 / (1 + gain / loss.replace(0, np.nan))
    prev = close.shift(1)
    true_range = pd.concat(
        [(d["High"] - d["Low"]).abs(), (d["High"] - prev).abs(), (d["Low"] - prev).abs()],
        axis=1,
    ).max(axis=1)
    d["ATR"] = true_range.rolling(14).mean()
    return d.dropna(subset=["SMA20", "SMA50", "AVG_VOLUME20", "RSI", "ATR"])


def score_row(row: pd.Series, params: Params) -> int:
    value = 35.0
    if row.Close > row.SMA20:
        value += 10 + min(7, (row.Close / row.SMA20 - 1) * 100)
    if row.SMA20 > row.SMA50:
        value += 9 + min(7, (row.SMA20 / row.SMA50 - 1) * 100)
    ratio = 0 if row.AVG_VOLUME20 <= 0 else row.Volume / row.AVG_VOLUME20
    if ratio > params.volume_confirmation:
        value += 8 + min(8, (ratio - params.volume_confirmation) * 8)
    if 48 <= row.RSI <= 68:
        value += 6 + max(0, 5 - abs(row.RSI - 58) / 2)
    if row.RSI > 72:
        value -= 15 + min(8, row.RSI - 72)
    return int(np.clip(round(value), 0, 100))


def trades_for(data: dict[str, pd.DataFrame], params: Params, start: pd.Timestamp,
               end: pd.Timestamp, fee_rate: float = 0.004, horizon: int = 5) -> pd.DataFrame:
    rows: list[dict] = []
    for symbol, frame in data.items():
        indices = frame.index[(frame.Date >= start) & (frame.Date < end)]
        for idx in indices:
            row = frame.loc[idx]
            score = score_row(row, params)
            if score < params.buy_score or row.Close <= 0 or row.ATR <= 0:
                continue
            stop = row.Close - row.ATR * params.atr_stop_multiplier
            target = row.Close + (row.Close - stop) * params.minimum_risk_reward
            future = frame.loc[idx + 1: idx + horizon]
            if future.empty:
                continue
            exit_price = float(future.iloc[-1].Close)
            exit_date = future.iloc[-1].Date
            outcome = "TIME"
            for _, candle in future.iterrows():
                if candle.Low <= stop:
                    exit_price, exit_date, outcome = stop, candle.Date, "STOP"
                    break
                if candle.High >= target:
                    exit_price, exit_date, outcome = target, candle.Date, "TARGET"
                    break
            net_return = exit_price / row.Close - 1 - fee_rate
            rows.append({
                "Symbol": symbol, "EntryDate": row.Date, "ExitDate": exit_date,
                "Entry": row.Close, "Exit": exit_price, "Score": score,
                "Outcome": outcome, "NetReturn": net_return,
            })
    return pd.DataFrame(rows)


def metrics(trades: pd.DataFrame) -> dict:
    if trades.empty:
        return {"trades": 0, "win_rate": 0.0, "average_return": -1.0, "max_drawdown": -1.0}
    ordered = trades.sort_values(["ExitDate", "Symbol"])
    equity = (1 + ordered.NetReturn.clip(lower=-0.99)).cumprod()
    drawdown = equity / equity.cummax() - 1
    return {
        "trades": int(len(trades)),
        "win_rate": float((trades.NetReturn > 0).mean()),
        "average_return": float(trades.NetReturn.mean()),
        "max_drawdown": float(drawdown.min()),
    }


def objective(result: dict) -> float:
    if result["trades"] < 20:
        return -999
    return result["average_return"] * 100 + result["win_rate"] - abs(result["max_drawdown"]) * 0.75


def evaluate_candidate(data: dict[str, pd.DataFrame], params: Params,
                       train_start: pd.Timestamp, cursor: pd.Timestamp) -> tuple:
    result = metrics(trades_for(data, params, train_start, cursor))
    return objective(result), params, result


def load_data(folder: Path) -> tuple[dict[str, pd.DataFrame], str]:
    output: dict[str, pd.DataFrame] = {}
    digest = hashlib.sha256()
    for file in sorted(folder.glob("*.csv")):
        raw = file.read_bytes()
        digest.update(file.name.encode())
        digest.update(raw)
        frame = pd.read_csv(file)
        frame.columns = [str(x).title() for x in frame.columns]
        required = {"Date", "Open", "High", "Low", "Close", "Volume"}
        if not required.issubset(frame.columns):
            continue
        frame["Date"] = pd.to_datetime(frame["Date"], utc=True).dt.tz_localize(None)
        frame = indicators(frame.reset_index(drop=True)).reset_index(drop=True)
        if len(frame) >= 80:
            output[file.stem.upper().replace(".JK", "")] = frame
    if not output:
        raise SystemExit("Tidak ada CSV valid dengan minimal 80 candle.")
    return output, digest.hexdigest()


def train(args: argparse.Namespace) -> None:
    folder = Path(args.data)
    output_dir = Path(args.output)
    output_dir.mkdir(parents=True, exist_ok=True)
    data, fingerprint = load_data(folder)
    first = min(x.Date.min() for x in data.values()).normalize()
    last = max(x.Date.max() for x in data.values()).normalize()
    folds: list[dict] = []
    all_oos: list[pd.DataFrame] = []
    cursor = first + pd.DateOffset(months=args.train_months)
    while cursor + pd.DateOffset(months=args.test_months) <= last + pd.DateOffset(days=1):
        train_start = cursor - pd.DateOffset(months=args.train_months)
        test_end = cursor + pd.DateOffset(months=args.test_months)
        with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as executor:
            ranked = list(executor.map(
                lambda params: evaluate_candidate(data, params, train_start, cursor),
                GRID,
            ))
        _, best, train_metrics = max(ranked, key=lambda x: x[0])
        oos = trades_for(data, best, cursor, test_end)
        oos_metrics = metrics(oos)
        if not oos.empty:
            oos["FoldStart"] = cursor
            all_oos.append(oos)
        folds.append({
            "train_start": str(train_start.date()), "test_start": str(cursor.date()),
            "test_end": str(test_end.date()), **asdict(best),
            **{f"train_{k}": v for k, v in train_metrics.items()},
            **{f"oos_{k}": v for k, v in oos_metrics.items()},
        })
        cursor = test_end
    if len(folds) < 3:
        raise SystemExit("Data terlalu pendek. Dibutuhkan minimal 3 fold out-of-sample.")
    oos_all = pd.concat(all_oos, ignore_index=True) if all_oos else pd.DataFrame()
    overall = metrics(oos_all)
    if overall["trades"] < 30:
        raise SystemExit(f"Hanya {overall['trades']} trade OOS; minimal 30 agar artefak boleh diekspor.")

    # Final parameters are the median of fold winners, reducing dependence on
    # one unusually good month.
    fold_frame = pd.DataFrame(folds)
    final = Params(
        int(round(fold_frame.buy_score.median())),
        int(round(fold_frame.watch_score.median())),
        float(fold_frame.volume_confirmation.median()),
        float(fold_frame.atr_stop_multiplier.median()),
        float(fold_frame.minimum_risk_reward.median()),
    )
    stamp = datetime.now(timezone.utc)
    strategy = {
        "Version": f"wf-{stamp:%Y%m%d-%H%M}",
        "MinimumRiskReward": final.minimum_risk_reward,
        "VolumeConfirmation": final.volume_confirmation,
        "AtrStopMultiplier": final.atr_stop_multiplier,
        "BuyScore": final.buy_score,
        "WatchScore": final.watch_score,
        "MaximumNormalPosition": 2_000_000,
        "MaximumSpeculativePosition": 500_000,
        "Training": {
            "Method": "walk-forward-v1", "TrainedAtUtc": stamp.isoformat(),
            "DataStart": str(first.date()), "DataEnd": str(last.date()),
            "OutOfSampleFolds": len(folds), "OutOfSampleTrades": overall["trades"],
            "OutOfSampleWinRate": overall["win_rate"],
            "OutOfSampleAverageReturn": overall["average_return"],
            "OutOfSampleMaxDrawdown": overall["max_drawdown"],
            "DataFingerprint": fingerprint,
        },
    }
    (output_dir / "strategy-trained.json").write_text(json.dumps(strategy, indent=2), encoding="utf-8")
    fold_frame.to_csv(output_dir / "walk-forward-report.csv", index=False)
    oos_all.to_csv(output_dir / "trades-out-of-sample.csv", index=False)
    (output_dir / "training-summary.json").write_text(
        json.dumps({"parameters": asdict(final), "out_of_sample": overall,
                    "folds": len(folds), "fingerprint": fingerprint}, indent=2),
        encoding="utf-8",
    )
    print(f"Strategy exported: {output_dir / 'strategy-trained.json'}")
    print(json.dumps(overall, indent=2))


def download(args: argparse.Namespace) -> None:
    import yfinance as yf
    out = Path(args.data)
    out.mkdir(parents=True, exist_ok=True)
    symbols = [x.strip().upper().replace(".JK", "") for x in Path(args.symbols).read_text().splitlines() if x.strip()]
    for index, symbol in enumerate(symbols, 1):
        try:
            frame = yf.download(f"{symbol}.JK", start=args.start, end=args.end,
                                auto_adjust=False, progress=False, threads=False)
            if isinstance(frame.columns, pd.MultiIndex):
                frame.columns = frame.columns.get_level_values(0)
            if not frame.empty:
                frame.reset_index()[["Date", "Open", "High", "Low", "Close", "Volume"]].to_csv(out / f"{symbol}.csv", index=False)
            print(f"{index}/{len(symbols)} {symbol}: {len(frame)} candle")
        except Exception as exc:
            print(f"{index}/{len(symbols)} {symbol}: gagal ({exc})")


def main() -> None:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    fetch = sub.add_parser("download")
    fetch.add_argument("--symbols", required=True)
    fetch.add_argument("--start", required=True)
    fetch.add_argument("--end", required=True)
    fetch.add_argument("--data", default="data")
    fit = sub.add_parser("train")
    fit.add_argument("--data", default="data")
    fit.add_argument("--output", default="output")
    fit.add_argument("--train-months", type=int, default=6)
    fit.add_argument("--test-months", type=int, default=1)
    fit.add_argument("--workers", type=int, default=max(1, min(8, (__import__("os").cpu_count() or 2) - 1)))
    args = parser.parse_args()
    download(args) if args.command == "download" else train(args)


if __name__ == "__main__":
    main()
