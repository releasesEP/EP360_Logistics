# CI/CD de EP360_Logistics_Admin

Flujo: `dev` (trabajo) -> Pull Request -> `prod` (despliegue). `kevs` y `magdas` son ramas personales.

## Workflows (`.github/workflows`)

| Archivo | Cuando corre | Que hace |
|---|---|---|
| `ci-build.yml` | PR hacia `dev` o `prod` | Compila (Release, `/restore`) y valida las vistas con `aspnet_compiler`. Las vistas son C# 5: nada de `?.` en `.cshtml`. |
| `cd-deploy-prod.yml` | push a `prod` | Job `compilar` (nube): compila, valida vistas, arma el artefacto con `debug="false"`. Job `desplegar` (runner del servidor): respaldo, `app_offline.htm`, Robocopy, reciclar pool, prueba de humo, borrar artefacto. |

## Servidor

- Servidor de IIS: **192.168.50.11** (la BD global vive en el .60, `192.168.50.60,5003`).
- Sitio: `C:\inetpub\Aplicaciones\EP360Logistics\Produccion`
- Application pool: `ep360logistics`
- Respaldos: `C:\inetpub\Aplicaciones\EP360Logistics\respaldos_eplogistics\` (ultimos 5 `.zip`, fuera del sitio para que `/MIR` no los toque).

## Runner

- Nombre: `eplogistics-runner-11`, carpeta `C:\runners\EP360_Logistics`, servicio de Windows como `NT AUTHORITY\NETWORK SERVICE`.
- Etiqueta propia `eplogistics`; el CD usa `runs-on: [self-hosted, eplogistics]` para que ningun otro runner tome el job.
- Registro (en el servidor, como administrador; el token de registro lo genera una cuenta admin de `releasesEP`: repo > Settings > Actions > Runners > New self-hosted runner):

```powershell
.\config.cmd --url https://github.com/releasesEP/EP360_Logistics --token <TOKEN> --name eplogistics-runner-11 --labels eplogistics --runasservice --windowslogonaccount "NT AUTHORITY\NETWORK SERVICE" --unattended
```

## Variables del repo (Actions > Variables)

| Variable | Valor |
|---|---|
| `RUTA_SITIO_IIS` | `C:\inetpub\Aplicaciones\EP360Logistics\Produccion` |
| `NOMBRE_APP_POOL` | `ep360logistics` |
| `URL_PRUEBA_HUMO` | `http://ep360logistics.eplogistics.com/` |

No hay secretos en GitHub: la configuracion real vive en el servidor.

## Configuracion que NO se despliega

`ConnectionStrings.config` y `AppSettings.config` estan fuera de git y viven solo en `RUTA_SITIO_IIS`; el Robocopy los excluye (`/XF`), igual que `App_Data` (`/XD`). El CD falla con un mensaje claro si no existen en el servidor. Crearlos una vez desde los `.example`:

- `ConnectionStrings.config`: `Data Source=192.168.50.60,5003;Initial Catalog=EP360_Logistics;Integrated Security=True;TrustServerCertificate=True` (identidad del pool).
- `AppSettings.config`: `AD_GrupoAdmins=ep360admins`; correo con las llaves `EmailSmtp*`.
- Cada valor de AppSettings se puede sobreescribir con una variable de entorno de **maquina** del mismo nombre (correo: `EMAIL_SMTP_*`). Tambien se leen de maquina `DB_CONNECTION_STRING` y `DB_CONNECTION_STRING_REPLICA` (opcional, replica al 11); este workflow **no** las escribe.

## Permisos y tropiezos conocidos

1. `NETWORK SERVICE` debe estar en Administradores locales del servidor (`net localgroup Administrators`): lo necesitan `Restart-WebAppPool` y, si algun dia se escriben variables de maquina, el registro (`Requested registry access is not allowed`).
2. IIS no ve variables de maquina nuevas hasta un `iisreset`; reciclar el pool no basta.
3. El login SQL del pool (`IIS APPPOOL\ep360logistics`, o la cuenta de dominio/equipo con que se conecte al .60) debe existir en `192.168.50.60\ep360` con permisos sobre `EP360_Logistics` (lectura/escritura + `EXECUTE` en sus SPs).
4. La prueba de humo acepta 200/301/302/401/403 (la app usa autenticacion de Windows); 404, 5xx o sin respuesta fallan el despliegue.
5. El artefacto se borra al terminar (cuota de Actions storage de la org).
6. Rollback: descomprimir el ultimo `sitio_*.zip` de `respaldos_eplogistics` sobre el sitio.
