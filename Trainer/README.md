# StockMate Strategy Trainer

Trainer berjalan di komputer dan menghasilkan `strategy-trained.json` yang bisa
diimpor melalui **Pengaturan → Impor strategi** di APK.

Metodenya walk-forward:

1. data sebelum bulan uji dipakai memilih parameter;
2. bulan berikutnya dikunci sebagai out-of-sample;
3. hasil prediksi diukur setelah 5 hari atau ketika stop/target tersentuh;
4. jendela digeser dan proses diulang;
5. strategi final hanya diekspor jika memiliki minimal 3 fold dan 30 trade
   out-of-sample.

Ini bukan machine learning yang menjamin profit. Tujuannya mengurangi overfit
dan membuktikan strategi pada data yang belum dilihat saat parameter dipilih.

## Persiapan

```powershell
cd Trainer
py -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
```

Unduh data IDX dari Yahoo Finance menggunakan daftar universe:

```powershell
python train.py download --symbols ..\stockmate-idx-universe.txt --start 2024-01-01 --end 2026-07-25
```

Atau letakkan CSV sendiri di `data/` dengan nama `BBRI.csv`, `TLKM.csv`, dan
seterusnya. Kolom wajib: `Date,Open,High,Low,Close,Volume`.

Jalankan training:

```powershell
python train.py train --data data --train-months 6 --test-months 1
```

Output:

- `output/strategy-trained.json`: artefak untuk APK;
- `output/walk-forward-report.csv`: hasil setiap fold;
- `output/trades-out-of-sample.csv`: seluruh prediksi yang benar-benar diuji;
- `output/training-summary.json`: ringkasan dan fingerprint dataset.

Jangan memilih strategi hanya dari win rate. Periksa jumlah trade, average
return setelah fee, dan maximum drawdown. Sebaiknya gunakan data minimal
12–24 bulan agar mencakup lebih dari satu kondisi pasar.
