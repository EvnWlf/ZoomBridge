# ZoomBridge (Zsave) 📹☁️

> **Agente de escritorio ligero, autónomo y de código abierto para automatizar el respaldo y sincronización de grabaciones locales de Zoom hacia SharePoint, OneDrive, Google Drive o almacenamiento local.**

---

## 🌟 Filosofía y Principios

- **Privacidad Absoluta (Zero-Telemetry / Local-First):** Ningún dato, bitácora, ruta o metadato de las reuniones sale de la computadora del usuario. Toda la operación ocurre de manera estricta y privada en el entorno local.
- **Agnosticismo de Proveedor:** Compatible con cualquier almacenamiento en la nube mediante carpetas locales sincronizadas (OneDrive, SharePoint Sync, Google Drive for Desktop, Nextcloud o discos locales).
- **Cero Configuración de APIs Cloud:** No requiere credenciales corporativas, tokens OAuth ni Azure AD Secrets. Se apoya en el cliente de sincronización nativo del usuario.
- **Idempotencia y Resiliencia:** Base de datos embebida SQLite (`zoombridge.db`) que asegura que ninguna reunión se transfiera dos veces y un inspector de procesos (`zTscoder.exe`) que evita corrupciones por transferencias prematuras.

---

## 🏗️ Arquitectura del Sistema

```
ZoomBridge/
├── Core/
│   ├── Config/
│   │   ├── AppConfig.cs            # Modelo de configuración serializable
│   │   └── ConfigManager.cs        # Gestión en %APPDATA%\ZoomBridge\config.json y Registro
│   ├── Storage/
│   │   ├── DatabaseContext.cs      # Persistencia SQLite embebida (%APPDATA%\ZoomBridge\zoombridge.db)
│   │   └── ProcessedRecording.cs   # Modelo de datos de reuniones transferidas
│   ├── Monitoring/
│   │   ├── ProcessInspector.cs     # Detección de zTscoder.exe, archivos .zoom y file locks
│   │   └── ZoomWatcherService.cs   # FileSystemWatcher con canal asíncrono y debounce
│   └── Transfer/
│       └── MeetingTransferService.cs # Traslado atómico, renombrado y reintentos
├── UI/
│   ├── RenamePromptWindow.xaml     # Diálogo flotante con cuenta regresiva para renombrar
│   ├── SettingsWindow.xaml         # Ventana de configuración de rutas y opciones
│   ├── HistoryWindow.xaml          # Visualizador del historial de reuniones respaldadas
│   └── TrayIconManager.cs          # Icono en el System Tray de Windows y menú contextual
├── installer/
│   └── zoombridge-setup.iss        # Script de instalación Inno Setup (un solo clic)
└── ZoomBridge.Tests/               # Suite de pruebas automatizadas xUnit (.NET 10)
```

---

## 🚀 Requisitos y Ejecución

- **Sistema Operativo:** Windows 10 / Windows 11 (64-bit)
- **Framework:** .NET 10 (LTS)

### Compilación y Ejecución en Desarrollo:

```powershell
# Compilar solución
dotnet build ZoomBridge.slnx

# Ejecutar pruebas
dotnet test

# Iniciar agente en modo desarrollo
dotnet run --project ZoomBridge.csproj
```

### Publicación Autónoma (Self-Contained):

```powershell
.\publish.ps1
```
Genera un ejecutable único con el runtime de .NET 10 integrado en `publish\win-x64\ZoomBridge.exe`.

---

## ⚙️ Menú en la Bandeja del Sistema (System Tray)

Al ejecutarse, ZoomBridge se oculta discretamente en la bandeja del sistema:
- **Estado en tiempo real:** Muestra si está monitoreando o transfiriendo.
- **Historial de grabaciones:** Consulta todas las grabaciones procesadas, tamaños y fechas, con opción de abrirlas directamente en el Explorador de Windows.
- **Configuración:** Cambia las carpetas de Zoom y Destino, ajusta el tiempo de espera para renombrar o activa el inicio con Windows.
- **Escanear pendientes ahora:** Procesa inmediatamente cualquier grabación existente que no haya sido respaldada previamente.
