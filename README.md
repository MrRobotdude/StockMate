# StockMate 1.6.13

## Perbaikan progres background 1.6.13

- Ambil/perbarui data manual memakai foreground service yang sama dengan scan
  terjadwal, sehingga proses tidak bergantung pada halaman Scanner tetap buka.
- Notifikasi progres tetap tersedia ketika UI ditutup dan meninggalkan status
  akhir untuk kondisi selesai, gagal, dihentikan, atau menunggu retry closing.
- Progres terakhir disimpan dan ditampilkan kembali saat Scanner dibuka.
- Pengaturan Background scanner menyediakan tombol untuk memeriksa izin dan
  channel notifikasi progres Android.

## Ringkasan keputusan 1.6.12

- Scanner Android dijadwalkan sekitar 12.15 dan 16.30 pada hari bursa.
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

- Dashboard menampilkan area harga dan jumlah lot pada subtitle rekomendasi.
- Scanner menampilkan harga beli ideal dan lot tanpa mengekspos skor internal.
- Kartu portofolio menampilkan tindakan dan tingkat keyakinan sejak kondisi tertutup.
- Alasan, risiko, target, dan stop tetap tersedia ketika kartu dibuka.

## Perbaikan scanner dan loading

- Shortlist hanya menampilkan sinyal BUY yang benar-benar mendapat alokasi kas dan lot.
- Kandidat yang tidak kebagian kas tetap tersedia sebagai WATCH untuk audit, bukan rekomendasi utama.
- Pager memakai informasi halaman di baris terpisah dan dua tombol berukuran sama.
- Tombol halaman memiliki guard sehingga nomor halaman tidak dapat melewati batas.
- Scanner memakai tombol vertikal yang lebih nyaman pada layar ponsel.
- Loading overlay memblokir interaksi selama pengambilan data, analisis, penyimpanan, validasi, dan kalkulasi halaman.

## Penyempurnaan UI 1.6.4

- Satu design system untuk card, tombol, bantuan, ukuran teks, dan pagination.
- Dashboard menampilkan angka dan tindakan utama; detail dibuka dengan mengetuk card.
- Scanner, portofolio, transaksi, detail posisi, Sync Up, onboarding, dan pengaturan memakai pola ringkas yang sama.
- Penjelasan panjang dipindahkan ke tombol `?` atau bagian card yang dapat dibuka.
- Tombol aksi memiliki tinggi, padding, warna, dan urutan yang konsisten.
- Pagination selalu memakai `Sebelumnya` dan `Berikutnya`.
- Detail expandable dibuat tanpa memindahkan view antar-parent sehingga aman untuk Android.

## Perbaikan 1.6.3

- Memperbaiki crash Android `The specified child already has a parent`
  saat **Ambil / perbarui data** maupun **Analisis snapshot** dijalankan.
- Pager proses teknis sekarang memakai satu container yang stabil dan tidak
  memasukkan ulang tombol yang masih terikat ke parent lama.

## Baru di 1.6.2

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

## Baru di 1.6.1

- Scanner memisahkan pengambilan data internet dari analisis strategi.
- Snapshot dapat dianalisis ulang setelah strategi hasil training diimpor,
  tanpa mengunduh ulang data pasar.
- Filter dan sorting transaksi ditambahkan, termasuk periode, sumber,
  BUY/SELL, nilai, fee, serta pagination.
- Sorting scanner dan portofolio kini berorientasi pada prioritas keputusan.
- Tombol pagination konsisten memakai label Sebelumnya/Berikutnya.
- Penjelasan utama dipindahkan ke tombol bantuan `?`.
- Bahasa Indonesia dan English dapat dipilih dari Pengaturan.

## Baru di 1.6.0

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

## Baru di 1.5.8

- Scanner berjalan penuh pada worker thread Android untuk mencegah `NetworkOnMainThreadException`.
- Tombol Stop menghentikan scan dengan aman dan mempertahankan checkpoint.
- Harga posisi diterapkan segera per simbol yang sukses, tanpa menunggu full scan selesai.
- Reset terpisah untuk scanner, transaction history, dan seluruh aplikasi.
- Dialog konfirmasi, informasi, serta input utama memakai modal visual StockMate.
- Empty state dan surface diperbarui agar layar kosong tetap terlihat matang.

## Perubahan 1.5.7

- Migrasi otomatis dari v1.5.6 dan Sync Up ulang tanpa menghapus data.
- Invested/modal, nilai pasar, unrealized, kas, dan total equity dipisahkan.
- Fee e-Statement dihitung dengan tarif broker agar modal posisi konsisten.
- Realized resmi Stockbit direkonsiliasi saat Sync Up; estimasi tetap dapat diaudit.
- PDF diproses di background, checkpoint snapshot dikurangi menjadi tiap 10 saham,
  dan update UI diperlambat maksimal sekitar 6 kali/detik agar tidak lag.
- HTTP 403 mencoba endpoint harga kedua dan retry dengan log teknis eksplisit.

- Sync Up mendeteksi SELL tanpa BUY/cost basis sebelumnya.
- Perolehan dari IPO, transfer masuk, atau sumber lain dapat dilengkapi dengan
  lot, harga perolehan, dan biaya opsional.
- Data tersebut disimpan sebagai `IPO_SYNC`, tetap aktif saat e-Statement
  diimpor ulang, dan tidak digandakan ketika dikoreksi.
- Realized dihitung ulang setelah seluruh cost basis lengkap. Sync Up tidak
  dapat diselesaikan selama masih ada penjualan tanpa modal.

## Perubahan dari 1.5.5

- Scanner pada akhir pekan otomatis memakai closing hari bursa terakhir.
- Universe fallback 99 tidak lagi dianggap sebagai full scan.
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
- Panel proses teknis menyimpan 30 event terbaru: sumber data, simbol, request,
  respons, parsing, retry, rate limit, timeout, checkpoint, dan durasi.
- Verifikasi closing dibatasi tiga kali; bila sumber data belum lengkap scanner
  melanjutkan dengan peringatan dan tidak menunggu tanpa batas.

- Instalasi baru tidak memiliki saldo atau portofolio contoh.
- Aplikasi wajib meminta impor e-Statement/Transaction History sebelum membuka dashboard.
- Scanner membagi universe menjadi batch berisi maksimal 100 saham.
- UI dan notifikasi menampilkan batch aktif, jumlah batch, saham aktif, progres total, berhasil, dan gagal.
- Checkpoint tetap disimpan per saham dan setiap akhir batch agar proses dapat dilanjutkan.

## Baru di 1.5.2

- File history yang sama dapat diterapkan ulang untuk merekonsiliasi transaksi manual.
- Setelah impor history, seluruh transaksi manual dinonaktifkan dari perhitungan portofolio dan dipindahkan ke arsip koreksi.
- Halaman transaksi secara default hanya menampilkan transaksi aktif.
- Ringkasan memisahkan nilai saham, kas tercatat, unrealized P/L, realized P/L, dan fee.
- Kas negatif diberi peringatan rekonsiliasi dan tidak dipakai untuk menyarankan penambahan posisi.
- Ringkasan menampilkan rekomendasi prioritas lintas tindakan: buka posisi baru, tambah, hold, take profit, reduce, atau sell.
- Semua label confidence diperjelas menjadi **Keyakinan rekomendasi**.
- Scanner menampilkan tahap proses, saham aktif, selesai/total, berhasil, gagal, persentase, dan progress bar determinate.
- Progres terakhir tetap terlihat ketika kembali ke halaman Scanner dan tidak kembali menjadi `0/0` saat selesai.
- Keputusan posisi mencakup stop loss tetap, trailing stop beserta persentasenya, dan rencana take profit sebagian/penuh.
- Tap kartu portofolio membuka halaman detail posisi dengan navigasi Back; Buy, Sell, dan pengaturan risiko tersedia di halaman tersebut.

## Baru di 1.5.1

- Memperbaiki error inferensi LINQ saat mengekstrak teks PDF e-Statement.
- Memperbaiki overload ambigu saat membuat notifikasi awal scanner.

## Fitur dari 1.5.0

- Progres scan terstruktur: tahapan, simbol aktif, berhasil/gagal/total, persen, progress bar, dan progress numerik pada notifikasi Android.
- Impor e-Statement PDF Stockbit sesuai format `Transaction History` (Trans Date, Due Date, Stock, Buy/Sell, Lot, Price, Buy/Sell Value, Sales Tax).
- Partial fill tetap disimpan sebagai transaksi berbeda; baris TOTAL, header, dan footer diabaikan.
- PDF Stockbit hanya mencantumkan sales tax. StockMate tidak mengklaim realized P/L sudah net seluruh brokerage fee.
- Portfolio Decision mengevaluasi posisi aktual dan history: HOLD, ADD, AVERAGE DOWN bertahap, TAKE PROFIT, REDUCE, atau SELL ALL/CUT LOSS.
- Setiap keputusan memuat ukuran tambah maksimum, stop, target, alasan, kondisi pembatalan, dan tingkat keyakinan.
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

`artifacts\release\StockMate-v1.6.12-release.apk`

Jangan kehilangan keystore atau password. Android hanya menerima update dengan
application ID dan signing key yang sama. Keystore sudah dikecualikan dari Git.

Untuk GitHub personal, push repository lalu simpan empat Actions secrets:

- `ANDROID_KEYSTORE_BASE64`
- `ANDROID_KEY_ALIAS`
- `ANDROID_KEY_PASSWORD`
- `ANDROID_KEYSTORE_PASSWORD`

Workflow `.github/workflows/android-release.yml` berjalan manual atau saat tag
seperti `v1.6.12` dipush.

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

Impor e-Statement PDF atau XLSX tidak diklaim tersedia. Jika Stockbit
menghasilkan format berbeda, ekspor/simpan sebagai CSV terlebih dahulu dan
periksa preview hasil impor, periode, lot, harga, fee, serta kas.

## Master seluruh IDX

- Saat startup dan sebelum scan, StockMate mencoba mengambil master emiten dari
  endpoint Listed Company IDX.
- Cache master dianggap baru selama tujuh hari.
- Pengaturan menampilkan jumlah, sumber, dan tanggal pembaruan.
- Tombol **Perbarui master IDX** memaksa refresh.
- Impor/ekspor CSV/TXT tetap tersedia sebagai fallback jika endpoint berubah.
- Daftar 99 saham hanya menjadi fallback darurat ketika belum ada cache dan
  sumber IDX tidak dapat diakses; scanner tidak lagi sengaja dibatasi ke daftar
  tersebut.

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
- Setelah setiap simbol, snapshot langsung disimpan sebagai checkpoint.
- Bila Android menghentikan aplikasi, scan berikutnya melanjutkan simbol yang
  belum berhasil dan tidak mengulang simbol yang sudah tersimpan.
- Snapshot mempunyai status `IN_PROGRESS`, `PARTIAL`, `COMPLETE`, atau
  `COMPLETE_WITH_ERRORS`, beserta daftar simbol gagal dan log ringkas.
- Analisis tidak dijalankan terhadap snapshot yang belum menyelesaikan seluruh
  antrean.
- Data satu sesi dipakai ulang untuk analisis berikutnya kecuali refresh paksa.
- Hanya enam snapshot terakhir yang disimpan agar penyimpanan terkendali.

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
bulanan, saham spekulatif, auto-scan, jeda request, universe, dan strategi JSON.
Hasil ekspor strategi bernama `stockmate_strategy.json`.

## Penyimpanan, rate limit, dan privasi

Data aplikasi disimpan lokal. Menghapus aplikasi dapat menghapus database
lokal; simpan Transaction History dan file penting di tempat lain.

Pemindaian seluruh IDX tetap tidak dapat dijamin bebas rate limit atau blokir
sementara. StockMate mengurangi risiko dengan satu request pada satu waktu,
delay, cache sesi, checkpoint, dan backoff. Endpoint gratis atau endpoint IDX
dapat berubah; impor universe manual tetap dipertahankan sebagai jalur
pemulihan.

Data market gratis dapat terlambat atau tidak lengkap. Scanner teknikal tidak
memasukkan semua berita, fundamental, corporate action, foreign flow, kondisi
IHSG, atau likuiditas order book. Selalu cocokkan timestamp dan harga di
Stockbit sebelum memasang order. Tidak ada hasil scan yang menjamin profit.

## Batas validasi paket

Source, referensi file, nama Android resource, XML/XAML/SVG, struktur solusi,
dan integritas ZIP diperiksa di lingkungan pembuatan paket. Toolchain .NET 10
Android tidak tersedia di lingkungan tersebut, sehingga build final tetap
harus divalidasi oleh `Setup-StockMate.ps1` atau Visual Studio pada komputer
yang memiliki workload MAUI Android.
## Catatan upgrade dari 1.5.9

Versi ini mendukung upgrade in-place. Gunakan `ApplicationId`
`id.stockmate.personal` yang sama dan jangan uninstall aplikasi lama jika ingin
mempertahankan transaksi, hasil scan, universe, input IPO, dan konfigurasi.

Menjalankan dari Visual Studio/Debug juga akan memperbarui instalasi yang sama
selama package ID dan signing debug yang dipakai tetap sama. Jika Android
menolak update karena signature berbeda, jangan uninstall: gunakan kembali
keystore/signing yang sama atau ekspor data dari versi lama terlebih dahulu.

Perubahan 1.5.9:

- pagination, pencarian, filter, dan sorting pada Scanner dan Portofolio;
- seluruh hasil universe tersimpan, bukan dipotong menjadi 15 kandidat;
- skor dibuat lebih granular agar tidak menumpuk pada 80/100;
- rekomendasi menampilkan area entry, jumlah lot, batas beli, stop, target, dan
  trailing stop bila relevan;
- rekomendasi 0 lot tidak lagi ditampilkan sebagai BUY yang dapat dieksekusi;
- label data memakai tanggal perdagangan dan Closing Sesi 1/2, bukan jam candle
  mentah dari provider.
