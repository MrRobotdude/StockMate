# StockMate 0.8.0

StockMate masih berada pada tahap pengembangan awal. Nomor versi publik memakai
format `0.MINOR.PATCH`: fitur yang sudah stabil untuk satu milestone digabung
dalam versi minor yang sama, sedangkan perbaikan kecil cukup menaikkan build
APK tanpa membuat versi publik baru.

## Yang tersedia di milestone 0.8

- Universe saham diminta langsung dari daftar resmi IDX setiap hari, disimpan
  sebagai cache, dan tidak lagi bergantung pada `idx-universe.txt` atau tombol
  impor universe manual. Scan tidak diteruskan bila sumber online dan cache
  belum memberi master minimal 500 saham.
- Impor e-Statement Stockbit memvalidasi jumlah baris serta total BUY, SELL,
  dan sales tax terhadap footer PDF. Statement lama hanya mengganti transaksi
  pada akun dan rentang tanggal yang sama; transaksi manual di luar periode
  tetap aktif.
- Setelah impor, posisi dibangun ulang, snapshot harga terakhir langsung
  dipasangkan bila tersedia, lalu Dashboard dan Portofolio menerima satu event
  refresh yang konsisten. Posisi baru tanpa harga ditampilkan sebagai belum
  terharga dan tidak pernah dianggap memiliki harga nol untuk keputusan jual.
- Tombol **Ekspor evaluasi CSV** menggantikan ekspor universe. CSV memuat data
  sebelum sinyal, order, hasil T+1, hasil swing, setup, risiko, rezim pasar,
  versi strategi, dan status riset untuk audit atau diagnosis trainer.
- Scanner mensyaratkan tren, momentum, volume, dan price action secara
  bersamaan serta menampilkan setup utama dan rezim IHSG. MA20 saja tidak dapat
  menghasilkan BUY.
- State hanya mempertahankan snapshot rolling terbaru karena snapshot lama
  menduplikasi hampir seluruh candle. Checkpoint tetap disimpan per batch 100
  saham, sementara update notifikasi rutin dikoaleskan agar UI tetap terlihat
  bergerak tanpa membebani Android.
- Bundle model v2.1.1 yang ditolak tidak dimasukkan. Runtime aplikasi tetap
  diberi label rule-based sampai ada bundle `READY_FOR_FORWARD_TEST` yang juga
  lulus parity test implementasi.

## Yang tersedia di milestone 0.7

- Sekitar 08.45 WIB memperbarui konteks sebelum pembukaan, memeriksa ulang
  rekomendasi pukul 07.00, dan menandai order yang gugur sebagai `DIBATALKAN`.
- Setelah scan closing 16.30 WIB memperbarui isu dan keputusan gabungan.
- Cakupan dibatasi ke portofolio, 20 kandidat teratas, dan konteks pasar.
- Detail memisahkan skor teknikal, penyesuaian isu, skor gabungan, sumber, dan waktu.
- Dampak isu biasa dibatasi, sedangkan corporate action/peristiwa berisiko
  tinggi dapat memberi veto konservatif dan tidak pernah mengalahkan stop-loss.

Feed gratis dapat terlambat atau tidak lengkap. Jika data tidak cukup, aplikasi
menyatakan keputusan tetap berbasis teknikal dan tidak mengarang sentimen.

### Evaluasi prediksi T+1 0.7.3

- Evaluasi prediksi berada pada halaman scroll tersendiri, lengkap dengan
  pencarian, filter status, ringkasan, dan pagination.
- Setiap rekomendasi menyimpan data sebelum keputusan, instruksi prediksi,
  realisasi OHLC hari bursa berikutnya, serta hasil akhir swing secara terpisah.
- Rekomendasi berulang untuk simbol yang sama tetap disimpan per tanggal sinyal,
  sehingga urutan HOPE hari T, T+1, dan seterusnya dapat dibandingkan.
- Status T+1 langsung diperbarui setelah snapshot baru selesai diunduh; tidak
  menunggu target, stop, atau batas holding 20 hari.
- Simulasi GFD mengikuti instruksi UI: opening di atas limit membatalkan order,
  bukan dianggap terisi ketika harga turun di tengah hari.
- Alasan keputusan memakai data terukur: kemiringan MA20, posisi MA20/MA50,
  momentum, RSI, rasio volume, kekuatan close, likuiditas, dan risk/reward net fee.
- Tren moving average saja tidak cukup untuk menghasilkan rekomendasi BUY.
- Rights issue, HMETD, private placement, dilusi, suspensi, default, fraud, dan
  investigasi menjadi veto konservatif untuk rekomendasi beli baru.

### Koreksi prediksi dan instruksi lot 0.7.2

- RSI yang sebelumnya terbalik sudah dikoreksi.
- ATR memakai true range, filter likuiditas memakai median nilai transaksi,
  dan ukuran lot menghitung risiko bersih termasuk fee beli/jual.
- Target dibulatkan secara konservatif agar risk/reward minimum tetap terpenuhi.
- Entry hanya berupa satu harga limit GFD; evaluasi membedakan order terisi dan
  tidak terisi serta memakai stop terlebih dahulu bila urutan intraday tidak diketahui.
- `REDUCE 50%` tidak lagi ditampilkan. Aplikasi menghasilkan jumlah lot pasti;
  untuk 3 lot, pengurangan separuh dibulatkan menjadi jual 2 lot dan sisa 1 lot.
- Model v2 yang berstatus `REJECTED_RESEARCH` tidak dimasukkan ke aplikasi karena
  bundle itu tidak memiliki bobot runtime yang layak. Fallback rule-based tetap
  diberi label jujur sampai ada bundle `READY_FOR_FORWARD_TEST`.
- Penyimpanan pengaturan dan pergantian bahasa tidak lagi mengganti root halaman
  ketika loading modal masih aktif. Callback progress selalu kembali ke UI thread.
- Teks dinamis, dialog, hasil impor, keputusan, prediksi, dan notifikasi mengikuti
  Bahasa Indonesia/English yang dipilih.

### Background scanner yang lebih tahan

- Penarikan data pagi dijadwalkan sekitar 07.00 WIB pada hari bursa dan
  langsung dilanjutkan dengan analisis serta keputusan portofolio agar
  rekomendasi sudah siap sebelum sesi pra-pembukaan.
- Ambil/perbarui data manual memakai foreground service yang sama dengan scan
  terjadwal, sehingga proses tidak bergantung pada halaman Scanner tetap buka.
- Notifikasi progres tetap tersedia ketika UI ditutup dan meninggalkan status
  akhir untuk kondisi selesai, gagal, dihentikan, atau menunggu retry closing.
- Progres terakhir disimpan dan ditampilkan kembali saat Scanner dibuka.
- Pengaturan Background scanner menyediakan tombol untuk memeriksa izin dan
  channel notifikasi progres Android.
- Android dapat mengirim ulang pekerjaan bila service direklamasi sistem.
- CPU wake lock dipakai maksimal dua jam dan dilepas segera setelah pekerjaan selesai.
- Channel progres berprioritas normal; hasil selesai/gagal memakai channel
  terpisah berprioritas tinggi dengan suara dan getaran.
- Notifikasi aktif menyediakan tombol `Hentikan` tanpa harus membuka aplikasi.
- Recovery melanjutkan checkpoint per batch 100 saham tanpa mengulang seluruh IDX.
- Force Stop tetap menghentikan alarm dan service sampai aplikasi dibuka lagi.

## Scanner, portofolio, dan keputusan

- Scanner Android dijadwalkan sekitar 07.00, 12.15, dan 16.30 pada hari bursa.
- Jadwal tetap dapat membangunkan aplikasi setelah UI ditutup atau dihapus
  dari recent apps, selama aplikasi tidak di-Force Stop oleh Android/pengguna.
- Bila data closing belum lengkap, pemeriksaan diulang otomatis 10 menit
  kemudian dan snapshot tidak dibuat dari data yang belum terverifikasi.
- Download dan analisis berjalan melalui foreground service dengan notifikasi.
- Jadwal dipasang kembali setelah perangkat reboot atau aplikasi diperbarui.
- Pengaturan menyediakan switch untuk mematikan atau menyalakan jadwal.

- Impor e-statement sekarang merge/idempotent: baris overlap dilewati dan tidak menggandakan transaksi.
- Rekonsiliasi kas dan realized resmi tidak lagi dihapus saat mengimpor statement baru.
- Dashboard dan Scanner membuka halaman detail saham yang memuat keputusan, posisi, entry, lot, stop, target, alasan, dan risiko.
- Input kode saham memakai pencarian universe IDX dan menolak simbol yang tidak terdaftar.

- Scanner dan Portofolio memakai keputusan posisi yang sama untuk saham yang
  sudah dimiliki; saham tersebut tidak dialokasikan ulang sebagai entry baru.
- Setiap rencana entry/posisi menyertakan entry, stop, target, ukuran, tingkat
  keyakinan, dan kondisi perubahan keputusan.
- Pengambilan data pasar dan analisis tidak lagi berjalan otomatis ketika
  aplikasi atau halaman Scanner dibuka.
- Loading proses memakai modal seluruh layar yang menutup tab menu dan menolak
  tombol Back sampai operasi selesai.

- Dashboard menampilkan satu harga limit dan jumlah lot pasti pada subtitle rekomendasi.
- Scanner menampilkan satu harga beli, lot, batas pembatalan, stop, dan target
  tanpa memberi rentang eksekusi.
- Kartu portofolio menampilkan tindakan dan tingkat keyakinan sejak kondisi tertutup.
- Alasan, risiko, target, dan stop tetap tersedia ketika kartu dibuka.

## Perbaikan scanner dan loading

- Shortlist hanya menampilkan sinyal BUY yang benar-benar mendapat alokasi kas dan lot.
- Kandidat yang tidak kebagian kas tetap tersedia sebagai WATCH untuk audit, bukan rekomendasi utama.
- Pager memakai informasi halaman di baris terpisah dan dua tombol berukuran sama.
- Tombol halaman memiliki guard sehingga nomor halaman tidak dapat melewati batas.
- Scanner memakai tombol vertikal yang lebih nyaman pada layar ponsel.
- Loading overlay memblokir interaksi selama pengambilan data, analisis, penyimpanan, validasi, dan kalkulasi halaman.

## Antarmuka

- Satu design system untuk card, tombol, bantuan, ukuran teks, dan pagination.
- Dashboard menampilkan angka dan tindakan utama; detail dibuka dengan mengetuk card.
- Scanner, portofolio, transaksi, detail posisi, Sync Up, onboarding, dan pengaturan memakai pola ringkas yang sama.
- Penjelasan panjang dipindahkan ke tombol `?` atau bagian card yang dapat dibuka.
- Tombol aksi memiliki tinggi, padding, warna, dan urutan yang konsisten.
- Pagination selalu memakai `Sebelumnya` dan `Berikutnya`.
- Detail expandable dibuat tanpa memindahkan view antar-parent sehingga aman untuk Android.

## Stabilitas scanner

- Memperbaiki crash Android `The specified child already has a parent`
  saat **Ambil / perbarui data** maupun **Analisis snapshot** dijalankan.
- Pager proses teknis sekarang memakai satu container yang stabil dan tidak
  memasukkan ulang tombol yang masih terikat ke parent lama.

## Closing, shortlist, dan evaluasi

- Closing dipilih otomatis: sesi 1 setelah midday close, sesi 2 setelah market
  close, dan hari bursa sebelumnya bila closing hari ini belum tersedia.
- Universe, hasil yang berhasil dianalisis, sinyal BUY, dan shortlist alokasi
  kas ditampilkan sebagai angka yang berbeda.
- Default scanner hanya menampilkan shortlist yang dapat dieksekusi. Semua
  hasil analisis tetap tersedia sebagai filter audit.
- Rencana lot dan prioritas dibatasi oleh kas aktual serta fee beli; total
  alokasi tidak dapat melebihi kas.
- Histori akurasi hanya mencatat rekomendasi BUY yang nyata, bukan saham WAIT.
- Dialog evaluasi menampilkan prediksi, target/stop, harga aktual, return,
  outcome, dan metadata walk-forward strategi aktif.
- Log teknis menyimpan 200 event serta memiliki filter dan pagination.

## Snapshot dan transaksi

- Scanner memisahkan pengambilan data internet dari analisis strategi.
- Snapshot dapat dianalisis ulang setelah strategi hasil training diimpor,
  tanpa mengunduh ulang data pasar.
- Filter dan sorting transaksi ditambahkan, termasuk periode, sumber,
  BUY/SELL, nilai, fee, serta pagination.
- Sorting scanner dan portofolio kini berorientasi pada prioritas keputusan.
- Tombol pagination konsisten memakai label Sebelumnya/Berikutnya.
- Penjelasan utama dipindahkan ke tombol bantuan `?`.
- Bahasa Indonesia dan English dapat dipilih dari Pengaturan.

## Build dan trainer

- Versi aplikasi dan build number tampil di Pengaturan dan berasal dari APK.
- Startup tidak lagi menulis ulang seluruh state jika tidak ada migrasi.
- Penyimpanan state dibuat ringkas, atomic, dan diserialisasi di background.
- Keputusan portofolio dicache berdasarkan revisi data; pindah Ringkasan/
  Portofolio tidak lagi menghitung dan menyimpan ulang tanpa perubahan.
- Delay startup buatan dihapus.
- `Build-Release.ps1` menghasilkan APK Release bertanda tangan yang dapat
  diinstal dan diberi nama berdasarkan versi.
- Workflow GitHub Actions membuat APK saat tag `v*` didorong dan dapat
  menambahkannya ke GitHub Release.
- Folder `Trainer` menyediakan walk-forward training/backtest paralel di
  komputer. Hasil `strategy-trained.json` dapat diimpor ke APK.
- APK memvalidasi jumlah fold/trade out-of-sample dan menampilkan win rate
  serta maximum drawdown artefak training.

## Worker dan reset

- Scanner berjalan penuh pada worker thread Android untuk mencegah `NetworkOnMainThreadException`.
- Tombol Stop menghentikan scan dengan aman dan mempertahankan checkpoint.
- Harga posisi diterapkan segera per simbol yang sukses, tanpa menunggu full scan selesai.
- Reset terpisah untuk scanner, transaction history, dan seluruh aplikasi.
- Dialog konfirmasi, informasi, serta input utama memakai modal visual StockMate.
- Empty state dan surface diperbarui agar layar kosong tetap terlihat matang.

## Rekonsiliasi portofolio

- Migrasi otomatis dari v1.5.6 dan Sync Up ulang tanpa menghapus data.
- Invested/modal, nilai pasar, unrealized, kas, dan total equity dipisahkan.
- Fee e-Statement dihitung dengan tarif broker agar modal posisi konsisten.
- Realized resmi Stockbit direkonsiliasi saat Sync Up; estimasi tetap dapat diaudit.
- PDF diproses di background, checkpoint snapshot disimpan tiap batch 100 saham,
  dan update UI/notifikasi rutin dikoaleskan agar tidak lag.
- HTTP 403 mencoba endpoint harga kedua dan retry dengan log teknis eksplisit.

- Sync Up mendeteksi SELL tanpa BUY/cost basis sebelumnya.
- Perolehan dari IPO, transfer masuk, atau sumber lain dapat dilengkapi dengan
  lot, harga perolehan, dan biaya opsional.
- Data tersebut disimpan sebagai `IPO_SYNC`, tetap aktif saat e-Statement
  diimpor ulang, dan tidak digandakan ketika dikoreksi.
- Realized dihitung ulang setelah seluruh cost basis lengkap. Sync Up tidak
  dapat diselesaikan selama masih ada penjualan tanpa modal.

## Universe IDX dan Sync Up

- Scanner pada akhir pekan otomatis memakai closing hari bursa terakhir.
- Snapshot pemulihan 963 kode hanya dipakai bila instalasi belum memiliki
  cache penuh dan sumber IDX sementara gagal; aplikasi tetap mencoba refresh
  resmi setiap hari.
- Master universe wajib berisi minimal 500 saham; kegagalan sinkronisasi tampil jelas.
- Snapshot 99 lama tidak digunakan sebagai hasil lengkap untuk universe yang lebih besar.
- Realized P/L diberi label estimasi dan dapat diaudit per transaksi jual.
- Audit realized menampilkan hasil jual, modal moving-average yang dialokasikan,
  fee yang tercatat, dan keterbatasan sales tax pada PDF e-Statement.

- Sync Up wajib setelah impor e-Statement untuk merekonsiliasi Trading Balance
  Stockbit; saldo sintetis dari history tidak lagi dianggap kas aktual.
- Scanner langsung memakai universe cache agar pembentukan batch tidak tertahan
  request master IDX.
- Proses dibagi menjadi batch 100 saham dan menampilkan progres keseluruhan serta
  progres batch.
- Panel proses teknis menyimpan 200 event terbaru: sumber data, simbol, request,
  respons, parsing, retry, rate limit, timeout, checkpoint, dan durasi.
- Verifikasi closing dibatasi tiga kali; bila sumber data belum lengkap scanner
  melanjutkan dengan peringatan dan tidak menunggu tanpa batas.

- Instalasi baru tidak memiliki saldo atau portofolio contoh.
- Aplikasi wajib meminta impor e-Statement/Transaction History sebelum membuka dashboard.
- Scanner membagi universe menjadi batch berisi maksimal 100 saham.
- UI dan notifikasi menampilkan batch aktif, jumlah batch, saham aktif, progres total, berhasil, dan gagal.
- Checkpoint disimpan setiap akhir batch agar proses dapat dilanjutkan tanpa menulis state pada setiap saham.

## Impor e-Statement dan keputusan posisi

- PDF yang terakhir diproses parser lama boleh direkonsiliasi ulang satu kali
  setelah upgrade. Sesudah memakai parser baru, fingerprint mencegah file yang
  sama diterapkan lagi, termasuk bila barisnya telah digantikan statement baru.
- Setelah impor history, hanya transaksi manual di dalam periode statement yang dinonaktifkan dan dipindahkan ke arsip koreksi; transaksi di luar periode tetap aktif.
- Halaman transaksi secara default hanya menampilkan transaksi aktif.
- Ringkasan memisahkan nilai saham, kas tercatat, unrealized P/L, realized P/L, dan fee.
- Kas negatif diberi peringatan rekonsiliasi dan tidak dipakai untuk menyarankan penambahan posisi.
- Ringkasan menampilkan rekomendasi prioritas lintas tindakan: buka posisi baru, tambah, hold, take profit, reduce, atau sell.
- Semua label confidence diperjelas menjadi **Keyakinan rekomendasi**.
- Scanner menampilkan tahap proses, saham aktif, selesai/total, berhasil, gagal, persentase, dan progress bar determinate.
- Progres terakhir tetap terlihat ketika kembali ke halaman Scanner dan tidak kembali menjadi `0/0` saat selesai.
- Keputusan posisi mencakup stop loss tetap, trailing stop beserta persentasenya, dan rencana take profit sebagian/penuh.
- Tap kartu portofolio membuka halaman detail posisi dengan navigasi Back; Buy, Sell, dan pengaturan risiko tersedia di halaman tersebut.

## Kompatibilitas

- Memperbaiki error inferensi LINQ saat mengekstrak teks PDF e-Statement.
- Memperbaiki overload ambigu saat membuat notifikasi awal scanner.

## Fondasi aplikasi

- Progres scan terstruktur: tahapan, simbol aktif, berhasil/gagal/total, persen, progress bar, dan progress numerik pada notifikasi Android.
- Impor e-Statement PDF Stockbit sesuai format `Transaction History` (Trans Date, Due Date, Stock, Buy/Sell, Lot, Price, Buy/Sell Value, Sales Tax).
- Partial fill tetap disimpan sebagai transaksi berbeda; baris TOTAL, header, dan footer diabaikan.
- PDF Stockbit hanya mencantumkan sales tax. StockMate tidak mengklaim realized P/L sudah net seluruh brokerage fee.
- Portfolio Decision mengevaluasi posisi aktual dan history: HOLD, ADD, AVERAGE DOWN, TAKE PROFIT, REDUCE, atau SELL ALL/CUT LOSS.
- Setiap keputusan memuat jumlah lot pasti, satu harga eksekusi, stop, target,
  alasan, kondisi pembatalan, dan skor keyakinan.
- Portfolio Decision tetap berbasis data harga-volume terbaru dan manajemen risiko; belum menggantikan analisis fundamental, berita, makro, atau verifikasi order di Stockbit.

StockMate adalah aplikasi .NET MAUI khusus Android untuk mencatat portofolio
saham Indonesia, merekonsiliasi transaksi dengan Transaction History broker,
memindai seluruh universe IDX secara bertahap, menyimpan snapshot closing, dan
mengevaluasi hasil scan.

StockMate bukan auto-trading. Aplikasi tidak meminta username, password, PIN,
OTP, atau token Stockbit dan tidak mengirim order ke broker.

## Menjalankan proyek

1. Ekstrak ZIP ke folder baru dan kosong.
2. Jalankan `Setup-StockMate.ps1` dari root paket.
3. Buka `StockMate.slnx`.
4. Pilih perangkat/emulator Android, lalu tekan F5.

Jika PowerShell memblokir script:

```powershell
powershell -ExecutionPolicy Bypass -File .\Setup-StockMate.ps1
```

Project hanya menargetkan `net10.0-android`. ZIP tidak membawa `bin`, `obj`,
atau `.csproj.user`, sehingga cache restore komputer lain tidak ikut terbawa.
Script setup membersihkan cache lokal, menjalankan workload restore, NuGet
restore, dan build validasi Android.

## Membuat APK Release installable

Jalankan:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-Release.ps1
```

Pada build pertama script membuat `stockmate-release.keystore`, meminta
password, melakukan publish `Release`, lalu menghasilkan:

`artifacts\release\StockMate-v0.8.0-release.apk`

Jangan kehilangan keystore atau password. Android hanya menerima update dengan
application ID dan signing key yang sama. Keystore sudah dikecualikan dari Git.

Untuk GitHub personal, push repository lalu simpan empat Actions secrets:

- `ANDROID_KEYSTORE_BASE64`
- `ANDROID_KEY_ALIAS`
- `ANDROID_KEY_PASSWORD`
- `ANDROID_KEYSTORE_PASSWORD`

Workflow `.github/workflows/android-release.yml` berjalan manual atau saat tag
seperti `v0.7.0` dipush.

## Training strategi di komputer

Project training terdapat di folder `Trainer`. Gunakan data minimal 12–24
bulan. Trainer memilih parameter pada jendela training, mengujinya pada bulan
berikut yang belum dilihat, lalu mengulang minimal tiga fold. Detail instalasi,
download data, output laporan, dan proses impor ada di `Trainer/README.md`.

## Startup dan branding

- Splash, app icon, dan halaman loading memakai branding StockMate.
- Portofolio dimuat asynchronous; UI tidak diblokir dengan
  `GetAwaiter().GetResult()`.
- Halaman loading menampilkan spinner, status proses, error, dan tombol coba
  lagi.
- Saat startup, StockMate mencoba memperbarui master emiten IDX. Jika sumber
  tidak tersedia, cache lokal tetap dipakai dan aplikasi masih dapat dibuka.
- Nama raw resource JSON memakai underscore, misalnya
  `strategy_example.json`, agar valid untuk Android.

## Portofolio dan kas

- Posisi dibentuk ulang dari transaksi BUY/SELL aktif.
- Average price pembelian memasukkan fee beli.
- Penjualan mengurangi lot, menghasilkan realized P/L, dan menambah kas bersih
  setelah fee jual.
- Kas direkonstruksi dari opening balance dan seluruh cash flow transaksi
  aktif. Ketika transaksi manual dikoreksi history, kas ikut terkoreksi.
- Dashboard menampilkan cash, equity, unrealized P/L, realized P/L, fee,
  konsentrasi, dan risiko terbuka.
- Harga semua posisi diperbarui dari candle terbaru di snapshot, bukan hanya
  saham yang lolos menjadi kandidat scanner.
- Buy/sell manual tetap tersedia untuk transaksi yang belum ada di history.

## Transaction History sebagai sumber mutlak

File Transaction History menjadi sumber kebenaran untuk periode yang dicakup:

1. transaksi OPENING sementara dinonaktifkan setelah history pertama;
2. transaksi manual dalam periode file dinonaktifkan dari perhitungan;
3. transaksi manual tetap tersimpan sebagai audit trail;
4. history baru yang tumpang tindih menggantikan history lama pada rentang itu;
5. fingerprint mencegah file identik diimpor dua kali;
6. posisi, average, realized P/L, fee, dan kas dihitung ulang;
7. laporan rekonsiliasi menampilkan transaksi manual yang dikoreksi.

Parser menerima CSV/TSV, termasuk file yang memiliki baris metadata sebelum
header. Variasi header Indonesia/Inggris yang dikenali mencakup tanggal/trade
time, symbol/stock code, buy-sell/transaction type, lot/volume/quantity,
price/matched price, fee, dan transaction/order/trade ID.

Contoh minimal:

```csv
Date,Symbol,Side,Lot,Price,Fee,Transaction ID
2026-07-24 09:15:00,BBRI,BUY,2,3650,1095,TRX-001
2026-07-25 10:10:00,BBRI,SELL,1,3720,930,TRX-002
```

E-Statement PDF Stockbit dengan tabel Transaction History didukung langsung.
Format PDF broker lain atau XLSX belum diklaim kompatibel; gunakan CSV/TSV dan
periksa hasil impor, periode, lot, harga, fee, serta kas.

## Master seluruh IDX

- Saat startup dan sebelum scan, StockMate mencoba mengambil master emiten dari
  endpoint Listed Company IDX.
- Master diminta ulang paling banyak sekali per hari; cache penuh tetap dipakai
  ketika sumber resmi sementara tidak tersedia.
- Pengaturan menampilkan jumlah, sumber, dan tanggal pembaruan.
- Tombol **Perbarui master IDX** memaksa refresh.
- Impor/ekspor universe manual sudah dihapus dari UI. Pengguna mengekspor data
  evaluasi prediksi, bukan daftar kode saham.
- Snapshot 963 kode bertanggal 25 Juli 2026 menjadi recovery darurat ketika
  belum ada cache dan sumber IDX tidak dapat diakses. Sumber serta tanggalnya
  tampil di Pengaturan dan bukan disamarkan sebagai refresh online baru.

## Closing, snapshot, dan scanner

- Sesi 1 Senin–Kamis memakai closing 12:00 WIB.
- Sesi 1 Jumat memakai closing 11:30 WIB.
- Sesi 2 memakai closing 16:00 WIB.
- Sebelum menarik seluruh universe, ketersediaan candle closing diverifikasi
  pada BBCA, BBRI, TLKM, dan ASII.
- Jika belum tersedia, foreground service polling satu menit sekali.
- Request saham berjalan satu per satu dengan jeda default 750 ms.
- HTTP 429 dianggap kondisi normal dan memakai backoff bertahap.
- Error per simbol dicatat; satu kegagalan tidak membatalkan seluruh scan.
- Snapshot disimpan sebagai checkpoint setiap akhir batch 100 saham agar
  recovery tetap tersedia tanpa menulis JSON besar pada setiap simbol.
- Bila Android menghentikan aplikasi, scan berikutnya melanjutkan simbol yang
  belum berhasil dan tidak mengulang simbol yang sudah tersimpan.
- Snapshot mempunyai status `IN_PROGRESS`, `PARTIAL`, `COMPLETE`, atau
  `COMPLETE_WITH_ERRORS`, beserta daftar simbol gagal dan log ringkas.
- Analisis tidak dijalankan terhadap snapshot yang belum menyelesaikan seluruh
  antrean.
- Data satu sesi dipakai ulang untuk analisis berikutnya kecuali refresh paksa.
- State mempertahankan snapshot rolling terbaru; hasil evaluasi historis tetap
  berada pada record rekomendasinya sendiri.

Scanner menghitung SMA20/SMA50, RSI, ATR, volume confirmation, entry area,
harga maksimal beli, stop loss, dua target, risk/reward, serta ukuran lot
berdasarkan batas risiko dan nilai posisi. Lima belas kandidat teratas
ditampilkan. Saham spekulatif tidak disertakan secara default.

## Berjalan saat layar mati

Scan dijalankan melalui Android foreground service dengan notifikasi permanen:

```text
StockMate sedang mengambil data
Mengambil data closing 426/958 • berhasil 421 • gagal 5
```

Notifikasi meminta izin pada Android 13+. Service berhenti otomatis setelah
selesai atau gagal. Foreground service lebih tahan daripada background task
biasa, tetapi Android/HiOS tetap dapat menghentikannya; checkpoint memastikan
proses dapat dilanjutkan. Untuk perangkat Tecno/HiOS, pengecualian optimasi
baterai mungkin tetap diperlukan.

## Evaluasi prediksi

- Riwayat menyimpan sesi, versi strategi, snapshot, cakupan universe, kandidat,
  harga awal, target, dan stop.
- Prediksi lama dievaluasi ketika harga pembanding tersedia.
- Outcome: `TARGET`, `STOP`, `POSITIVE`, atau `NEGATIVE`.
- Analisis ulang untuk session key dan versi strategi yang sama memperbarui
  record tersebut, bukan membuat duplikat.
- Outcome yang sudah dievaluasi dipertahankan.

## Pengaturan

Pengguna dapat mengubah fee beli/jual, risiko per transaksi, batas rugi
bulanan, saham spekulatif, auto-scan, jeda request, dan strategi JSON.
Hasil ekspor strategi bernama `stockmate_strategy.json`.

## Penyimpanan, rate limit, dan privasi

Data aplikasi disimpan lokal. Menghapus aplikasi dapat menghapus database
lokal; simpan Transaction History dan file penting di tempat lain.

Pemindaian seluruh IDX tetap tidak dapat dijamin bebas rate limit atau blokir
sementara. StockMate mengurangi risiko dengan satu request pada satu waktu,
delay, cache sesi, checkpoint, dan backoff. Bila endpoint resmi IDX berubah,
aplikasi mempertahankan cache penuh terakhir dan mencoba pembaruan lagi pada
proses berikutnya.

Data market dan feed isu gratis dapat terlambat atau tidak lengkap. Analisis isu
belum menggantikan pemeriksaan laporan keuangan, corporate action, foreign flow,
kondisi IHSG, atau likuiditas order book. Selalu cocokkan sumber, timestamp, dan
harga di Stockbit sebelum memasang order. Tidak ada hasil scan yang menjamin
profit.

## Batas validasi paket

Source, referensi file, nama Android resource, XML/XAML/SVG, struktur solusi,
dan integritas ZIP diperiksa di lingkungan pembuatan paket. Toolchain .NET 10
Android tidak tersedia di lingkungan tersebut, sehingga build final tetap
harus divalidasi oleh `Setup-StockMate.ps1` atau Visual Studio pada komputer
yang memiliki workload MAUI Android.

## Catatan upgrade

Versi ini mendukung upgrade in-place. Gunakan `ApplicationId`
`id.stockmate.personal` yang sama dan jangan uninstall aplikasi lama jika ingin
mempertahankan transaksi, hasil scan, universe, input IPO, dan konfigurasi.

Menjalankan dari Visual Studio/Debug juga akan memperbarui instalasi yang sama
selama package ID dan signing debug yang dipakai tetap sama. Jika Android
menolak update karena signature berbeda, jangan uninstall: gunakan kembali
keystore/signing yang sama atau ekspor data dari versi lama terlebih dahulu.

Fitur kompatibilitas yang tetap dipertahankan:

- pagination, pencarian, filter, dan sorting pada Scanner dan Portofolio;
- seluruh hasil universe tersimpan, bukan dipotong menjadi 15 kandidat;
- skor dibuat lebih granular agar tidak menumpuk pada 80/100;
- rekomendasi menampilkan area entry, jumlah lot, batas beli, stop, target, dan
  trailing stop bila relevan;
- rekomendasi 0 lot tidak lagi ditampilkan sebagai BUY yang dapat dieksekusi;
- label data memakai tanggal perdagangan dan Closing Sesi 1/2, bukan jam candle
  mentah dari provider.
