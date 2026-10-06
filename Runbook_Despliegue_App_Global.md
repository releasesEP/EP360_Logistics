# Despliegue de la app de administración global (EP360_Logistics_Admin)

Funciona igual que el portal EP360: un push a la rama **`prod`** dispara el CD (`.github/workflows/cd-deploy-prod.yml`), que compila en la nube de GitHub y copia el resultado al sitio de IIS con un runner autoalojado instalado en el servidor 60.

> Valores de ejemplo usados abajo (ajústalos si prefieres otros): sitio `EP360 Logistics Admin`, pool `ep360logistics`, carpeta `C:\inetpub\Aplicaciones\ep360\EP360_Logistics_Admin`, puerto `5002`.

## Qué hace cada workflow

| Workflow | Cuándo | Qué hace |
|---|---|---|
| `ci-build.yml` | push a `dev` y PR hacia `prod` | Compila en Release y valida **todas las vistas Razor** con `aspnet_compiler` (el `.csproj` no las compila al construir, así que sin esto un error de vista pasaría el build). Con configuración de relleno; no toca ninguna base. |
| `cd-deploy-prod.yml` | push a `prod` | **compilar** (nube): mismo build, carpeta limpia sin `.cs`, apaga `debug`. **desplegar** (servidor 60): escribe variables de máquina, pone `app_offline.htm`, copia con Robocopy, lo quita, confirma el estado del pool y borra el artefacto. |

## Una sola vez: preparar el servidor 60

### 1. IIS
1. Características: **ASP.NET 4.8** y **Windows Authentication** (sin esto el sitio no puede identificar al administrador).
   ```powershell
   Install-WindowsFeature Web-Asp-Net45, Web-Windows-Auth
   ```
2. Application Pool y sitio (PowerShell como administrador):
   ```powershell
   Import-Module WebAdministration
   New-Item "C:\inetpub\Aplicaciones\ep360\EP360_Logistics_Admin" -ItemType Directory -Force
   New-WebAppPool -Name "ep360logistics"
   Set-ItemProperty "IIS:\AppPools\ep360logistics" -Name managedRuntimeVersion -Value "v4.0"
   Set-ItemProperty "IIS:\AppPools\ep360logistics" -Name managedPipelineMode -Value "Integrated"
   New-Website -Name "EP360 Logistics Admin" -Port 5002 -PhysicalPath "C:\inetpub\Aplicaciones\ep360\EP360_Logistics_Admin" -ApplicationPool "ep360logistics"
   ```
3. **Autenticación**: Windows activada y Anónima apagada (la app exige usuario de Windows y revisa que pertenezca al grupo `ep360admins`):
   ```powershell
   Set-WebConfigurationProperty -Filter /system.webServer/security/authentication/anonymousAuthentication -Name enabled -Value $false -PSPath IIS:\ -Location "EP360 Logistics Admin"
   Set-WebConfigurationProperty -Filter /system.webServer/security/authentication/windowsAuthentication -Name enabled -Value $true  -PSPath IIS:\ -Location "EP360 Logistics Admin"
   ```
4. Abrir el puerto 5002 en el firewall de Windows (como se hizo con 5001 para Ep360Api).

### 2. Runner de GitHub para este repositorio
Cada repositorio tiene su propio runner (EP360, Ep360Api, Ep360Go ya tienen). Crear uno para `EP360_Logistics`:
1. En GitHub: **EP360_Logistics → Settings → Actions → Runners → New self-hosted runner → Windows**. Seguir sus comandos en `C:\runners\Ep360Logistics`, con etiqueta por defecto `self-hosted`.
2. Instalarlo como servicio con la **misma cuenta y permisos que los runners existentes** (en EP360 corre como `NT AUTHORITY\NETWORK SERVICE`, con lectura de `C:\Windows\System32\inetsrv\config` y pertenencia al grupo Administradores para controlar IIS). Si el servicio se reinicia después de cambiar permisos, tomar los nuevos permisos.
3. Debe quedar "Idle" (verde) en la página de Runners.

### 3. Secretos y variables del repositorio
**Settings → Secrets and variables → Actions**

| Tipo | Nombre | Valor |
|---|---|---|
| Secret | `EP360LOGISTICS_CONNECTION_STRING` | `Data Source=192.168.50.60\ep360;Initial Catalog=EP360_Logistics;Integrated Security=True;TrustServerCertificate=True` |
| Secret | `EP360LOGISTICS_CONNECTION_STRING_REPLICA` | `Data Source=192.168.50.11;Initial Catalog=EP360_Logistics;Integrated Security=True;TrustServerCertificate=True` (déjalo **vacío** para escribir solo en el 60) |
| Variable | `RUTA_SITIO_IIS` | `C:\inetpub\Aplicaciones\ep360\EP360_Logistics_Admin` |
| Variable | `NOMBRE_APP_POOL` | `ep360logistics` |

- Las cadenas **no llevan usuario ni contraseña**: la app entra con la identidad del Application Pool (Integrated Security).
- Los nombres de las variables de máquina son **propios** (`EP360LOGISTICS_*`) a propósito: el portal EP360 está en el mismo servidor y ya usa `DB_CONNECTION_STRING`; compartir el nombre haría que una app leyera la base de la otra. El CD aborta si falta el secreto principal.
- Active Directory no necesita variables nuevas: la app reutiliza `AD_Servidor`, que el CD de EP360 ya deja en el servidor. Si algún día hay que dar usuario y contraseña de lectura de AD, van como variables de máquina `AD_Usuario` y `AD_Password` (nunca en el repositorio).

### 4. Permisos SQL (`Database/12b_PermisosAppGlobal.sql`)
Correr **en el 60 y en el 11**, cada uno por separado, como administrador, cambiando `@cuenta`:
- **En el 60**: `IIS APPPOOL\ep360logistics` (el pool ya debe existir en IIS, paso 1).
- **En el 11**: la **cuenta de máquina del 60**, `EPLOGISTICS\EPL1-EP360SERVER$` (con el `$`). Un pool con ApplicationPoolIdentity sale a la red con esa cuenta, no con el nombre del pool.

Es lo mínimo (SELECT y EXECUTE sobre los esquemas `dir`, `seg`, `sync` y `flota`), sin `db_owner`.

### 5. Miembros del grupo de AD
Quien deba entrar a la app tiene que estar en el grupo `ep360admins` (el mismo que administra EP360). Sin pertenecer, ve la pantalla de "sin acceso".

## Primer despliegue

1. Mezclar la rama con estos archivos a `prod` (Pull Request `dev → prod`; el CI debe salir en verde).
2. El push a `prod` dispara el CD: ver **Actions → CD - Desplegar a IIS (prod)**. Los dos jobs deben terminar en verde.
3. Probar `http://192.168.50.60:5002/`:
   - Pide credenciales de Windows y entra al **Inicio** (muestra el estado de la base global).
   - **Personas**, **Accesos a portales**, **Sucursales** cargan datos.
   - **Copia en el 11**: si pusiste la cadena de réplica, debe salir "Igual" en todas las tablas.
4. Si EP360 va a mostrar el enlace a «Accesos a portales», poner en su `AppSettings.config` `AdminGlobal_Url` = `http://192.168.50.60:5002/Accesos`.

## Si algo falla

| Síntoma | Causa probable |
|---|---|
| El job `desplegar` no arranca | El runner está apagado o no está registrado para este repositorio. |
| `Falta el secreto EP360LOGISTICS_CONNECTION_STRING` | No se creó el secreto (paso 3). |
| 401 al abrir el sitio | Windows Authentication no está activada o Anónima sigue encendida (paso 1.3). |
| "Sin acceso" aunque inicies sesión | No estás en el grupo `ep360admins`, o el servidor no alcanza AD. |
| Error de base al abrir una pantalla | Faltan los permisos SQL del paso 4, o el pool tiene otro nombre. |
| "Copia en el 11" no puede leer el 11 | Falta el login de la cuenta de máquina en el 11 (paso 4), o el 11 está apagado. |
| Se ve la página de mantenimiento y no se quita | El Robocopy no terminó; revisar el log del paso "Copiar archivos al sitio". |

**Volver atrás:** este despliegue no cambia ninguna base. Para regresar a la versión anterior, en GitHub abre la ejecución anterior del workflow y usa **Re-run all jobs**, o revierte el commit en `prod`.
