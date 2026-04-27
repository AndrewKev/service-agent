# service-agent

## Deskripsi Singkat

**service-agent** adalah REST API berbasis ASP.NET Core 6 untuk memanajemen systemd service di Linux melalui HTTP endpoint. Setiap endpoint dilindungi oleh API key yang di-generate otomatis setiap kali aplikasi dijalankan.

---

## Installation

1. **Build & Publish**

   Jalankan perintah berikut untuk compile dan publish project:

   ```bash
   dotnet publish -c Release -f net6.0 -r linux-x64 --self-contained false -o ./publish
   ```

2. **Copy ke Server**

   Copy seluruh isi folder `./publish` ke direktori `/var/www/service-agent` di server.

3. **Buat systemd service**

   Buat file konfigurasi systemd di `/etc/systemd/system/service-agent.service` dengan isi berikut:

   ```ini
   [Unit]
   Description=service-agent .NET
   After=network.target

   [Service]
   WorkingDirectory=/var/www/service-agent
   ExecStart=/usr/bin/dotnet /var/www/service-agent/service-agent.dll
   Restart=always
   RestartSec=10
   SyslogIdentifier=service-agent
   User=www-data
   Environment=ASPNETCORE_ENVIRONMENT=Production
   Environment=ASPNETCORE_URLS=http://0.0.0.0:3333

   # Override ServiceMonitoring config
   Environment=ServiceMonitoring__Enabled=false
   Environment=ServiceMonitoring__PollingIntervalSeconds=30
   Environment=ServiceMonitoring__CommandTimeoutSeconds=10
   Environment=ServiceMonitoring__ManagementBaseUrl=http://SERVICE_AGENT_MANAGEMENT_HOST:5037
   Environment=ServiceMonitoring__RegisteredServicesEndpoint=/api/agent/registered-services
   Environment=ServiceMonitoring__AlertEndpoint=/api/alert
   Environment=ServiceMonitoring__ServerId=PUT-SERVER-GUID-HERE

   [Install]
   WantedBy=multi-user.target
   ```

   **note**: `ServiceMonitoring__Enabled` sengaja dibuat `false` dulu. Aktifkan monitoring alert setelah `service-agent-management` berjalan, agent sudah didaftarkan di management, dan management sudah berhasil melakukan koneksi ke agent.

4. **Aktifkan dan Jalankan Service**

   Jalankan perintah berikut:

   ```bash
   sudo systemctl daemon-reload
   sudo systemctl enable service-agent
   sudo systemctl start service-agent
   ```

5. **Cek API Key**

   Setelah service berjalan, lihat log untuk mendapatkan API key yang di-generate otomatis:

   ```bash
   sudo journalctl -u service-agent --no-pager | grep "API KEY"
   ```

6. **Konfigurasi sudo untuk www-data**

    Agar service-agent dapat menjalankan perintah systemctl, tee, dan journalctl tanpa password, tambahkan konfigurasi berikut pada file sudoers:

    - Jalankan perintah berikut di terminal server untuk membuka file sudoers dengan editor yang aman:

       ```bash
       sudo visudo
       ```

    - Tambahkan baris berikut di bagian **paling bawah** file sudoers (setelah semua baris yang sudah ada):

       ```
       www-data ALL=(root) NOPASSWD: /usr/bin/tee *, /usr/bin/systemctl daemon-reload, /usr/bin/systemctl enable *, /usr/bin/systemctl restart *, /usr/bin/systemctl stop *, /usr/bin/systemctl show *, /usr/bin/journalctl *
       ```

    - Simpan dan keluar dari editor (`Ctrl+X` jika menggunakan nano, atau `:wq` jika menggunakan vi/vim).

    > Konfigurasi ini diperlukan agar proses `www-data` (user yang menjalankan service-agent) dapat mengeksekusi perintah `systemctl`, `tee`, dan `journalctl` dengan hak akses root tanpa memerlukan password, termasuk kebutuhan background polling untuk membaca state service via `systemctl show`.
    >
    > Alternatif untuk endpoint log: berikan akses baca journal langsung ke user runtime (mis. masukkan ke group `systemd-journal`), sehingga endpoint log bisa jalan tanpa sudo.

---

## Authentication

Semua endpoint memerlukan header `X-Api-Key` dengan nilai API key yang muncul di console/log saat aplikasi pertama kali start. API key ini di-generate ulang setiap kali aplikasi di-restart.

---

## Service Monitoring (Polling Alert)

`service-agent` sekarang mendukung background polling untuk memantau service systemd terdaftar dan mengirim alert ke `service-agent-management`.

Behavior utama:

- Polling berjalan setiap 30 detik (default, bisa diubah via konfigurasi).
- Agent mengambil daftar service terdaftar dari endpoint management.
- Agent hanya mengecek service yang terdaftar.
- Jika state service tidak sehat, agent mengirim `POST /api/alert` ke management.
- Telegram tetap dikirim oleh `service-agent-management`, bukan oleh `service-agent`.

### Tahapan Mengaktifkan Alert

Ikuti urutan ini agar fitur alert berjalan dengan benar:

1. **Pastikan `service-agent-management` sudah berjalan**

   Jalankan aplikasi `service-agent-management` terlebih dahulu dan pastikan endpoint management bisa diakses dari server tempat `service-agent` berjalan.

   Jika management berjalan di mesin berbeda, jangan gunakan `127.0.0.1` pada konfigurasi agent. Gunakan IP atau domain yang bisa dijangkau oleh server agent, contoh:

   ```ini
   Environment=ServiceMonitoring__ManagementBaseUrl=http://192.168.1.10:5037
   ```

   Jika management masih berjalan di local/debug Windows dan agent berjalan di WSL, gunakan IP Windows host dari sisi WSL, bukan `127.0.0.1`.

2. **Daftarkan agent di `service-agent-management`**

   Start `service-agent`, lalu ambil API key agent dari log:

   ```bash
   sudo journalctl -u service-agent --no-pager | grep "API KEY"
   ```

   Daftarkan server/agent tersebut di `service-agent-management` dengan IP, port agent, dan API key yang benar. Satu management bisa mengelola beberapa agent di beberapa server, jadi pastikan data IP/port/API key disesuaikan untuk masing-masing server.

3. **Pastikan management bisa connect ke agent**

   Dari `service-agent-management`, lakukan health check atau koneksi ke agent yang sudah didaftarkan. Jangan aktifkan monitoring alert sebelum management berhasil connect ke agent.

   Jika API key agent berubah karena agent restart, update kembali API key di data server management.

4. **Isi `ServerId` agent sesuai data di management**

   Ambil ID server dari record server yang sudah dibuat di `service-agent-management`, lalu pasang ke konfigurasi systemd `service-agent`:

   ```ini
   Environment=ServiceMonitoring__ServerId=PUT-SERVER-GUID-HERE
   ```

   Nilai ini dipakai agent saat meminta daftar service terdaftar dan saat mengirim alert.

5. **Aktifkan `ServiceMonitoring` di systemd service-agent**

   Setelah semua langkah di atas berhasil, ubah konfigurasi systemd dari:

   ```ini
   Environment=ServiceMonitoring__Enabled=false
   ```

   menjadi:

   ```ini
   Environment=ServiceMonitoring__Enabled=true
   ```

   Reload systemd dan restart agent:

   ```bash
   sudo systemctl daemon-reload
   sudo systemctl restart service-agent
   ```

6. **Verifikasi log monitoring**

   Cek log agent untuk memastikan polling berjalan:

   ```bash
   sudo journalctl -u service-agent -f
   ```

   Jika muncul error `Connection refused`, biasanya `ManagementBaseUrl` belum bisa dijangkau dari server agent, management belum berjalan di host/port tersebut, atau firewall masih menolak koneksi.

### Konfigurasi

Tambahkan/atur section berikut di `appsettings.json`:

```json
"ServiceMonitoring": {
  "Enabled": false,
  "PollingIntervalSeconds": 30,
  "CommandTimeoutSeconds": 10,
  "ManagementBaseUrl": "http://127.0.0.1:5000",
  "RegisteredServicesEndpoint": "/api/agent/registered-services",
  "AlertEndpoint": "/api/alert",
  "ServerId": "PUT-SERVER-GUID-HERE"
}
```

Keterangan:

- `Enabled`: mengaktifkan/menonaktifkan monitoring.
- `PollingIntervalSeconds`: interval polling.
- `CommandTimeoutSeconds`: timeout command `systemctl`.
- `ManagementBaseUrl`: base URL `service-agent-management`.
- `RegisteredServicesEndpoint`: endpoint daftar service terdaftar.
- `AlertEndpoint`: endpoint alert di management.
- `ServerId`: ID server (`servers.id`) di database management.

### Kontrak Header ke Management

Saat agent memanggil endpoint management (`registered-services` dan `alert`), agent mengirim:

- `X-Api-Key`: API key runtime milik `service-agent`.
- `X-Server-Id`: nilai `ServerId` dari konfigurasi.

Catatan penting:

- API key agent berubah setiap restart.
- Pastikan `servers.api_key` di management selalu sinkron dengan API key terbaru agent agar validasi antar-service tetap berhasil.

---

## API Endpoints


### Health Check

| Method | Path     | Deskripsi                      |
|--------|----------|-------------------------------|
| GET    | /health  | Cek apakah service berjalan   |

#### Contoh Request

```
GET /health HTTP/1.1
Host: localhost:5555
X-Api-Key: <API_KEY>
```

#### Contoh Response Sukses (HTTP 200)

```
{"success":true,"message":"Connection success"}
```

#### Contoh Response Gagal (HTTP 500)

```
{"success":false,"message":"Connection failed","error":"<pesan exception>"}
```

#### Contoh Response Gagal Autentikasi (HTTP 401)

Tidak ada body JSON. Response 401 dihasilkan oleh filter autentikasi, bukan controller.

---


### Agent — Service Management

| Method | Path                                         | Deskripsi                        |
|--------|----------------------------------------------|----------------------------------|
| POST   | /agent/service/restart/{serviceName}         | Restart sebuah systemd service   |
| POST   | /agent/service/stop/{serviceName}            | Stop sebuah systemd service      |
| GET    | /agent/service/status/{serviceName}          | Cek status sebuah systemd service|
| GET    | /agent/service/logs/{serviceName}            | Stream log service dari journalctl |
| GET    | /agent/service/systemd/{serviceName}         | Ambil isi konfigurasi systemd    |
| PUT    | /agent/service/systemd/{serviceName}         | Edit konfigurasi systemd, reload |

#### Contoh Request & Response

##### Restart Service

```
POST /agent/service/restart/nginx HTTP/1.1
Host: localhost:5555
X-Api-Key: <API_KEY>
```

**Response Sukses (HTTP 200):**
```
{"success":true,"message":"Service 'nginx' restarted successfully."}
```

**Response Gagal (HTTP 400):**
```
{"success":false,"message":"Failed to restart service 'nginx'","error":"<output error dari systemctl>"}
```

##### Stop Service

```
POST /agent/service/stop/nginx HTTP/1.1
Host: localhost:5555
X-Api-Key: <API_KEY>
```

**Response Sukses (HTTP 200):**
```
{"success":true,"message":"Service 'nginx' stopped successfully."}
```

**Response Gagal (HTTP 400):**
```
{"success":false,"message":"Failed to stop service","error":"<output error dari systemctl>"}
```

**Response Timeout (HTTP 500):**
```
{"success":false,"message":"Timeout: systemctl stop took too long to complete."}
```

##### Status Service

```
GET /agent/service/status/nginx HTTP/1.1
Host: localhost:5555
X-Api-Key: <API_KEY>
```

**Response (HTTP 200, baik sukses maupun gagal):**
```
// Service ditemukan / running
{
   "success": true,
   "output": "<raw output dari systemctl status>",
   "error": "",
   "exitCode": 0
}

// Service tidak ditemukan / stopped
{
   "success": false,
   "output": "<raw output dari systemctl status>",
   "error": "<stderr jika ada>",
   "exitCode": 3
}
```

##### Stream Service Logs (Realtime)

```
GET /agent/service/logs/nginx?lines=200&follow=true HTTP/1.1
Host: localhost:5555
X-Api-Key: <API_KEY>
```

Query parameter:

- `lines` (opsional, default `200`): jumlah baris log awal yang dikirim saat koneksi dibuka.
- `follow` (opsional, default `true`): jika `true`, koneksi tetap terbuka untuk menerima log baru secara realtime.

**Response Sukses (HTTP 200):**

Response berupa `text/plain` stream (bukan JSON) dari output `journalctl -u <serviceName>`.

Contoh potongan stream:

```
Apr 18 10:15:31 host nginx[123]: starting worker process
Apr 18 10:15:32 host nginx[123]: worker process is running
...
```

**Response Gagal sebelum stream dimulai:**

- HTTP 400 untuk input tidak valid (mis. format `serviceName` salah atau `lines` di luar batas).
- HTTP 403 jika permission membaca journal tidak cukup.
- HTTP 404 jika unit/journal service tidak ditemukan.

Response gagal berbentuk JSON:

```
{"success":false,"message":"Unable to access logs for service 'nginx'.","error":"<detail error>"}
```

Catatan:

- Saat `follow=true`, endpoint sengaja menjaga koneksi tetap terbuka (long-lived HTTP connection).
- Jika client memutus koneksi, proses pembacaan log di server akan dihentikan.
- Untuk reverse proxy seperti Nginx, nonaktifkan buffering pada route ini agar stream tidak tertahan.

##### Get systemd Config

```
GET /agent/service/systemd/nginx HTTP/1.1
Host: localhost:5555
X-Api-Key: <API_KEY>
```

**Response Sukses (HTTP 200):**
```
{"success":true,"config":"<isi lengkap file .service>"}
```

**Response Gagal (HTTP 400):**
```
{"success":false,"message":"Failed to retrieve systemd configuration","error":"<output error>"}
```

##### Edit systemd Config

```
PUT /agent/service/systemd/nginx HTTP/1.1
Host: localhost:5555
X-Api-Key: <API_KEY>
Content-Type: application/json

{
   "config": "[Unit]\nDescription=nginx..."
}
```

> **Catatan:** Field `config` di request body berisi isi lengkap file `.service` dalam bentuk string.

**Response Sukses (HTTP 200):**
```
{"success":true,"message":"Config updated and service 'nginx' restarted successfully."}
```

**Response Gagal (HTTP 400):**
```
// Gagal saat menulis file
{"success":false,"step":"write_config","error":"<output error>"}

// Gagal saat daemon-reload
{"success":false,"step":"daemon_reload","error":"<output error>"}

// Gagal saat restart service
{"success":false,"step":"restart","error":"<output error>"}
```

---

## Cara Mendapatkan API Key

Setelah service dijalankan, API key akan muncul di log aplikasi. Gunakan perintah berikut untuk melihatnya:

```bash
sudo journalctl -u service-agent --no-pager | grep "API KEY"
```

---
