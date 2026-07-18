# MonitorSwitcher

MonitorSwitcher adalah aplikasi Windows untuk menyimpan dan mengganti profil konfigurasi monitor dengan cepat. Aplikasi ini berjalan dari system tray dan menyimpan profil logis dalam file `.profile.json`.

Cocok untuk setup multi-monitor yang sering berubah, misalnya:

- monitor utama saja
- monitor utama + monitor vertikal
- monitor utama + monitor landscape
- semua monitor aktif
- hanya monitor tertentu

## Screenshot

![Screenshot MonitorSwitcher](sampel.png)

## Cara Pakai

1. Jalankan aplikasi `MonitorSwitcher`.
2. Atur mode monitor di Windows sesuai kondisi yang ingin disimpan.
   - Contoh: aktifkan monitor utama saja, extend semua monitor, atau aktifkan monitor vertikal.
3. Klik ikon MonitorSwitcher di system tray.
4. Klik **Save Current Profile**.
5. Masukkan nama profil.
6. Untuk mengganti mode monitor, klik ikon tray lagi lalu pilih profil yang ingin dipakai.

Profil yang sudah tersimpan dapat di-rename atau dihapus dari menu tray.

## Cara Menjalankan dari Source

Pastikan sudah menginstall:

- Windows
- .NET SDK 8.0 atau lebih baru

Jalankan dari folder proyek:

```powershell
dotnet run
```

Build release:

```powershell
dotnet build -c Release
```

Publish executable Windows x64:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

Hasil publish akan berada di folder:

```text
bin\Release\net8.0-windows\win-x64\publish
```

## Mode CLI

Selain lewat tray, aplikasi juga bisa dipakai lewat command line:

```powershell
MonitorSwitcher.exe save "NamaProfil"
MonitorSwitcher.exe load "NamaProfil"
```

Perintah tersebut akan memakai file:

```text
NamaProfil.profile.json
```

Profil binary `.config` versi lama tetap dapat dibaca sebagai fallback.

## Local API

API aktif otomatis bersama tray app dan hanya listen di `127.0.0.1:47777`.

```powershell
curl http://127.0.0.1:47777/profile/3
```

Method `GET` dan `POST` didukung. Nomor profil mengikuti awalan nama file, sehingga endpoint Android tidak berubah ketika nama lengkap profil berubah.

## Teknis

- Bahasa: C#
- Framework: .NET 8
- Target: `net8.0-windows`
- UI: Windows Forms system tray + WPF popup
- API monitor: Windows Display Configuration API melalui `User32.dll`
- File profil: JSON berisi identitas monitor dan susunan display logis
- Resolver: membaca ulang ID/path display Windows setiap profil diterapkan
- API lokal: `http://127.0.0.1:47777/profile/{id}`

File penting:

- `Program.cs` - entry point aplikasi dan mode CLI
- `TrayApplicationContext.cs` - ikon dan behavior system tray
- `TrayUI.xaml` / `TrayUI.xaml.cs` - tampilan popup tray
- `LogicalProfileCapture.cs` - menyimpan kondisi monitor sebagai profil logis
- `DisplayTopologyResolver.cs` - memetakan monitor ke enumerasi Windows terbaru
- `LogicalProfileApplier.cs` - memvalidasi dan menerapkan profil
- `*.profile.json` - profil monitor yang tersimpan

## Catatan

Profil dibangun ulang memakai enumerasi Windows terbaru saat dipanggil. Monitor yang termasuk dalam profil tetap harus terdeteksi Windows.
