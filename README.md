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
   Environment=ASPNETCORE_URLS=http://0.0.0.0:5555

   [Install]
   WantedBy=multi-user.target
   ```

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

---

## Authentication

Semua endpoint memerlukan header `X-Api-Key` dengan nilai API key yang muncul di console/log saat aplikasi pertama kali start. API key ini di-generate ulang setiap kali aplikasi di-restart.

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

