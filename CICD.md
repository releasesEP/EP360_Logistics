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

`ConnectionStrings.config` y `AppSettings.config` estan fuera de git y viven solo en `RUTA_SITIO_IIS`; el Robocopy los excluye (`/XF`), igual que `App_Data` (`/XD`). Si no existen (primer deploy), el CD los crea con estos valores y, si ya existen, no los toca. Para cambiarlos se editan a mano en el servidor:

- `ConnectionStrings.config`: `Data Source=192.168.50.60,5003;Initial Catalog=EP360_Logistics;Integrated Security=True;TrustServerCertificate=True` (identidad del pool).
- `AppSettings.config`: `AD_GrupoAdmins=ep360admins`; correo con las llaves `EmailSmtp*`.
- Cada valor de AppSettings se puede sobreescribir con una variable de entorno de **maquina** del mismo nombre (correo: `EMAIL_SMTP_*`). Tambien se leen de maquina `EP360LOGISTICS_CONNECTION_STRING` y `EP360LOGISTICS_CONNECTION_STRING_REPLICA` (la segunda es opcional); este workflow **no** las escribe. Los nombres son **propios** a proposito (el codigo antes leia `DB_CONNECTION_STRING`): en el 11 viven mas apps (Help Desk, Intranet, EPTemplates, Startpoint, ...) y si alguna define esa variable de maquina, esta app se conectaria a SU base.

### Replicacion al 11 (opcional)

La app escribe primero en el 60 (maestro) y repite en la copia del 11, que es **local** a este servidor. Para activarla, agregar en el `ConnectionStrings.config` **del servidor** (el CD no lo toca) la linea:

```xml
<add name="CadenaSQLReplica" connectionString="Data Source=192.168.50.11;Initial Catalog=EP360_Logistics;Integrated Security=True;TrustServerCertificate=True" providerName="System.Data.SqlClient" />
```

Sin esa linea (ni la variable de maquina) escribe solo en el 60. Antes hay que correr en las dos bases los scripts `Database/10`, `10c` y `11c`. El estado se ve en la pantalla **Copia en el 11**.

## Permisos y tropiezos conocidos

1. `NETWORK SERVICE` debe estar en Administradores locales del servidor (`net localgroup Administrators`): lo necesitan `Restart-WebAppPool` y, si algun dia se escriben variables de maquina, el registro (`Requested registry access is not allowed`).
2. IIS no ve variables de maquina nuevas hasta un `iisreset`; reciclar el pool no basta.
3. Permisos SQL (`Database/12b_PermisosAppGlobal.sql`, se corre en las dos bases cambiando `@cuenta`): en el **60** la app entra con la **cuenta de maquina del 11**, `EPLOGISTICS\EPL1-APPSERVER0$` (el nombre se corta a 15 caracteres y lleva `$`; ese login **ya existe** en el 60, falta su usuario y permisos en `EP360_Logistics`); en el **11** (la copia es local) entra el pool, `IIS APPPOOL\ep360logistics`. Son SELECT y EXECUTE sobre los esquemas `dir`, `seg`, `sync` y `flota`, sin `db_owner`.
4. La prueba de humo acepta 200/301/302/401/403 (la app usa autenticacion de Windows); 404, 5xx o sin respuesta fallan el despliegue.
5. El artefacto se borra al terminar (cuota de Actions storage de la org).
6. Rollback: descomprimir el ultimo `sitio_*.zip` de `respaldos_eplogistics` sobre el sitio.
