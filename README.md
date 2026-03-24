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

#### Contoh Response Sukses

```
{"status":"Healthy"}
```

#### Contoh Response Gagal

```
{"error":"Unauthorized"}
```

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

**Response Sukses:**
```
{"message":"Service nginx restarted successfully"}
```

**Response Gagal:**
```
{"error":"Service not found"}
```

##### Stop Service

```
POST /agent/service/stop/nginx HTTP/1.1
Host: localhost:5555
X-Api-Key: <API_KEY>
```

**Response Sukses:**
```
{"message":"Service nginx stopped successfully"}
```

**Response Gagal:**
```
{"error":"Service not found"}
```

##### Status Service

```
GET /agent/service/status/nginx HTTP/1.1
Host: localhost:5555
X-Api-Key: <API_KEY>
```

**Response Sukses:**
```
{"service":"nginx","status":"active"}
```

**Response Gagal:**
```
{"error":"Service not found"}
```

##### Get systemd Config

```
GET /agent/service/systemd/nginx HTTP/1.1
Host: localhost:5555
X-Api-Key: <API_KEY>
```

**Response Sukses:**
```
{"service":"nginx","config":"[Unit]\n..."}
```

**Response Gagal:**
```
{"error":"Service not found"}
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

**Response Sukses:**
```
{"message":"Config updated and service reloaded"}
```

**Response Gagal:**
```
{"error":"Failed to update config"}
```

---

## Cara Mendapatkan API Key

Setelah service dijalankan, API key akan muncul di log aplikasi. Gunakan perintah berikut untuk melihatnya:

```bash
sudo journalctl -u service-agent --no-pager | grep "API KEY"
```

---

## Catatan Penting

- File yang dibuat hanya `README.md`, tidak perlu mengubah file kode apapun.
- Port default adalah `5555` sesuai konfigurasi systemd, dan bisa diubah di environment variable `ASPNETCORE_URLS`.
- Pastikan semua perintah instalasi sudah tertulis dengan benar dan berurutan.
