# Plan de producción: EP360 con la base global (EP360_Logistics)

Estado al 2026-10-06. Todo lo de abajo se comprobó con consultas de **solo lectura** contra el servidor 60; nada de producción se tocó.

## Idea del plan (decidida por el dueño del proyecto)

La base **EP360_Traceability** (copia ya migrada y probada) pasa a ser la base de producción de EP360. **No se corren scripts sobre `PortalEP`**: si todo funciona con la copia, la aplicación en producción simplemente apunta a ella, y `PortalEP` queda como respaldo y camino de regreso. Se despliega también la app de administración global (EP360_Logistics_Admin).

EP360 todavía **no está en uso diario real**: esto es una mejora que se está construyendo, no una migración de una operación en marcha. Eso permite una ventana de corte relajada.

## 1. Qué hay hoy

| Pieza | Dónde | Estado |
|---|---|---|
| **PortalEP** (producción actual) | servidor 60, instancia `ep360` | Sin cambios. 144 MB, 17 requerimientos, 159 usuarios, 436 clientes. |
| **EP360_Traceability** (futura producción) | mismo servidor y misma instancia | Migrada y probada: vistas sobre la global, permisos por módulo, perfiles, acceso desde la global. Ya tiene a los dos pools (`ep360` y `ep360api`) como `db_owner`. |
| **EP360_Logistics** (global) | servidor 60 (maestro) y 11 (copia) | Lista: directorio, accesos a portales, replicación al 11. |
| **App de administración global** | solo en tu laptop | Por desplegar. |

## 2. Qué se comprobó que NO es problema

- **No hay que migrar datos hacia la global.** Los 436 clientes de producción son los mismos ids ya enlazados; los 159 usuarios de producción son los de la copia menos 5 usuarios de prueba (ids 169 a 173).
- **Ep360Api no usa ningún procedimiento modificado.** Se leyó su repositorio (rama `prod`): llama a 25 procedimientos y ninguno está entre los 20 que cambiaron. Sus consultas leen las vistas, que conservan nombres y columnas. Su único procedimiento relacionado con la global es `sp_ObtenerSucursales`.
- **El pipeline de EP360 no cambia.** `cd-deploy-prod.yml` ya inyecta `DB_CONNECTION_STRING` y demás variables; la global se alcanza con nombres de tres partes dentro de la misma instancia. No hay variables de entorno nuevas obligatorias.
- **Los 15 scripts de EP360 no se corren en producción**: ya están aplicados en la copia, que es lo que se promueve. Los de `Pruebas/` tampoco.

## 3. Cómo se hace el cambio de base: dos opciones

| | **A. Renombrar las bases** (recomendada) | **B. Cambiar las cadenas de conexión** |
|---|---|---|
| Cómo | `PortalEP` → `PortalEP_anterior` y `EP360_Traceability` → `PortalEP` | Cambiar el secreto `DB_CONNECTION_STRING` del portal **y** la cadena de Ep360Api para que apunten a `EP360_Traceability` |
| Portal y Ep360Api | No cambia ninguna cadena ni secreto | Hay que cambiar las dos |
| Permisos | Los usuarios de los pools ya están en la copia | Igual |
| Reversa | Renombrar de vuelta | Volver a poner las cadenas anteriores |
| Cuidado | Necesita acceso exclusivo un instante (sitios detenidos) | Se pueden olvidar sitios o configuraciones que apunten a la base vieja (por ejemplo la API) |

Con la opción A no se puede olvidar ningún consumidor, porque todos siguen usando el nombre `PortalEP`. En ambas, **el código nuevo debe desplegarse junto con el cambio de base**: no es compatible con la base vieja.

> **Decisión del dueño (2026-10-06): se usa la opción B.** El portal y Ep360Api se conectan a `EP360_Traceability` cambiando la cadena de conexión. Consecuencias a vigilar: (1) hay que cambiar el secreto `DB_CONNECTION_STRING` del portal en GitHub **y** la cadena de Ep360Api (es otro repositorio y otro pool); (2) cualquier otra herramienta que apunte a `PortalEP` seguirá viendo la base vieja; (3) `PortalEP` queda intacta como camino de regreso (volver a poner las cadenas anteriores). También se decidió **dejar** los usuarios 169 a 173 y el rol y departamento «PEGATRON».

## 4. Qué falta

### A. Permisos SQL en la global (en trabajo)
Ni `IIS AppPool\ep360` ni `IIS APPPOOL\ep360api` tienen usuario en `EP360_Logistics`. Script listo: `Database/12_PermisosProduccion.sql` (mínimo necesario, sin `db_owner`). Se corre antes de usar el portal real.

### B. Datos de la copia antes de promoverla
La copia tiene 4 diferencias con producción, todas menores: `Bitacora_InicioSesion` (282 contra 278), `TraduccionCache` (235 contra 251), `Rol` (23 contra 24) y `Departamento` (37 contra 38). Además:
1. **Datos nuevos de producción desde la copia**: si se captura algo en `PortalEP` antes del corte, hay que llevarlo a la copia. Se hace un chequeo de diferencias por tabla justo antes del corte (hoy solo difieren las 4 tablas de arriba). Si hay diferencias en tablas de negocio, se migran por id.
2. **Limpiar artefactos de prueba** en la copia: los usuarios 169 a 173, el rol y el departamento «PEGATRON», los registros de bitácora de las pruebas. Decisión pendiente: **dejar o desactivar esos 5 usuarios** (siguen sin resolver).
3. **Revisar que ningún dato de prueba quedó** en la copia ni en la global.

### C. Despliegue de la app de administración global (workflow de Kevin adoptado)
Sin ella nadie puede dar acceso a portales, ni crear usuarios externos, ni sincronizar AD. El CI/CD es el de Kevin Aparicio (rama `ci/cicd-workflows`, documentado en `CICD.md`): la app corre en el **servidor 11**, con su runner (`eplogistics-runner-11`), configuración real en el servidor, respaldo del sitio, prueba de humo y reversa. Falta, en el servidor: el sitio y pool de IIS con autenticación de Windows, el runner registrado, las tres variables del repositorio y los permisos SQL (`Database/12b_PermisosAppGlobal.sql`, en el 60 con la cuenta de máquina del 11 y en el 11 con el pool). Se conserva un cambio propio: las variables de máquina de la app se llaman `EP360LOGISTICS_CONNECTION_STRING` y `EP360LOGISTICS_CONNECTION_STRING_REPLICA` (en el 11 viven más apps que podrían definir `DB_CONNECTION_STRING`).

### C2. Dos cadenas de conexión, dos repositorios (comprobado)
- **Portal (EP360)**: el CD de su repositorio escribe la variable de máquina `DB_CONNECTION_STRING` desde el secreto de GitHub `DB_CONNECTION_STRING`. Se lee en vivo, no se cachea.
- **Ep360Api**: es **otro repositorio** (`releasesEP/Ep360Api`) y su CD escribe otra variable, `EP360API_CONNECTION_STRING`, desde **su propio secreto** `EP360API_CONNECTION_STRING`.
- Hay que cambiar **los dos secretos** y desplegar **los dos** (o editar a mano la variable de máquina en el servidor, pero el secreto debe actualizarse también o el siguiente despliegue la revierte).
- Entre el cambio de uno y otro el handheld escribiría en la base vieja mientras el portal lee la nueva: la ventana debe tener a Ep360Api detenido hasta el final.

### C3. Métricas de login y último acceso (futuro, no bloquea el corte)
Hoy EP360 guarda su bitácora de inicios de sesión (`Bitacora_InicioSesion`) y el último acceso (`UsuarioEP360.fechaUltimoAcceso`) en su propia base. La idea es llevarlos a la global (vigilancia de login y panel de métricas de todos los portales). Camino sugerido: tablas de bitácora en la global, una pantalla de métricas en la app global, y que cada portal registre allá; primero en paralelo con lo local y después cambiando la lectura. No es necesario para el primer corte.

### D. Flujo de git
Los cambios están en `bd-global` y `magdas` de EP360, no en `dev` ni `prod`. Camino: `bd-global` a `dev`, PR a `prod` (el CD se dispara con el push a `prod`). **No unir a `dev` hasta tener fecha de corte.**

### E. Funcionalidad que conviene cerrar antes
1. **Sucursales a la global** (ver sección 7).
2. Pruebas completas de pantalla de EP360 con un usuario normal y con uno administrador.

## 5. Secuencia del corte (con la opción A)

**Antes**
1. Respaldo completo (`COPY_ONLY`, verificado con `RESTORE VERIFYONLY`) de `PortalEP`, de `EP360_Traceability` y de `EP360_Logistics`.
2. Permisos SQL de la global (sección 4A) corridos y verificados.
3. App global desplegada y probada, con la replicación al 11 funcionando.
4. Chequeo de diferencias entre `PortalEP` y la copia; migrar lo que falte.
5. Fecha acordada, aviso a quien use el portal y el handheld.

**Durante**
1. Detener el portal (`app_offline.htm`) y el pool de Ep360Api.
2. Último chequeo de diferencias y respaldo final de ambas.
3. Renombrar `PortalEP` a `PortalEP_anterior` y `EP360_Traceability` a `PortalEP`. Poner en línea.
4. `bd-global` a `dev`, PR a `prod`, esperar el CD.
5. Arrancar Ep360Api.
6. Humo: administrador y usuario normal; Usuarios, un requerimiento, un recibo, el dashboard, y la API del handheld.

**Reversa** (si en 30 minutos no está estable): detener los sitios, renombrar de vuelta `PortalEP_anterior` a `PortalEP`, volver a desplegar la versión anterior de `prod`. No se pierde nada que estuviera en `PortalEP`; lo capturado después del corte se rescata de la copia renombrada.

## 6. Qué cambia en el uso de «pruebas»

Después del corte la copia **deja de ser un ambiente de pruebas**: es producción. Y la **global es compartida**: cualquier prueba que escriba en ella afecta a producción. Para seguir probando cosas nuevas habrá que crear un ambiente aparte (una copia de la base de EP360 y **una global de pruebas separada**, por ejemplo `EP360_Logistics_Pruebas`). A decidir cuando se acerque el corte.

## 7. Sucursales: pasarlas a la app global (HECHO en código, falta correr los scripts)

1. **Global**: script con `dir.sp_InsertarSucursal`, `sp_ActualizarSucursal`, `sp_DesactivarSucursal` y `sp_ReactivarSucursal` (hoy solo hay lectura y `sp_ObtenerOCrearSucursal`).
2. **App global**: pantalla de Sucursales (lista, alta, edición, baja y reactivación), con el estilo actual.
3. **EP360**: quitar `SucursalesController`, la parte de escritura de `SucursalService`, `Views/Sucursales`, la entrada del menú de Catálogos y la tarjeta del resumen. EP360 conserva la tabla local `SucursalEP360` como traductor de ids.
4. **Detalle técnico**: cuando se cree una sucursal nueva en la global, EP360 necesita su fila local; se crea de forma perezosa en el procedimiento de lectura de sucursales (que también usa Ep360Api).

## 8. Orden recomendado

1. Sucursales a la global (cerrar funcionalidad).
2. Pruebas completas en pantalla de EP360.
3. Permisos SQL de la global (en curso) y despliegue de la app global con su workflow.
4. Limpieza de la copia y decisión sobre los 5 usuarios de prueba.
5. Fecha de corte, ventana y reversa lista.
