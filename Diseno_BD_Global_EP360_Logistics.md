# Diseño BD global de directorio: EP360 Logistics (borrador v1)

Fecha: 2026-10-02. Basado en el inventario de los repos EP360, EP360Balance, HelpDesk y EPLogisticsIntranet.
Estado: **propuesta para revisión**. No hay scripts todavía. Nombre de BD tentativo: `EP360_Logistics`.

## 1. Decisiones ya tomadas

| Tema | Decisión |
|---|---|
| Maestro | Servidor 60 (`.\ep360`). Único donde se escribe. Ids generados solo ahí. |
| Espejo | Servidor 11, solo lectura, por replicación transaccional. Después del maestro. |
| Lectura | Cada portal lee la copia de su propio servidor, sin linked servers. |
| Login de externos | Correo y contraseña propios, guardados en la global. |
| Red | El 60 es el servidor **público**; el 11 es **siempre local** (red interna). Toda app que se hace pública se migra al 60 y su login externo consulta al 60. |
| Permisos | Por portal, en su propia BD, ligados al id global de la persona. |
| Contacto a usuario | Un contacto puede escalar a usuario externo sin cambiar de id. |
| Balance | Funciona con AD (sus usuarios internos son `UsuarioAD`). Si un fragmento se publica, quién entra sigue sin definirse: el modelo soporta contactos de la cuenta y/o clientes finales. |
| Replicación al 11 | **Todo**: `dir`, `seg` (con hashes) y `sync`. La global existe en ambas instancias solo para evitar latencia; el 60 sigue siendo el único donde se escribe. |
| Método de validación | Primero se crea la global en el 60 y se prueba con **una copia del código de EP360 y una copia de su BD** (sandbox). Se hacen ahí las modificaciones y, cuando todo funciona, se replica el patrón a Balance, HelpDesk, Intranet y los portales nuevos. |

## 2. Principios de diseño

1. **Una persona, un id.** Usuarios de AD, usuarios externos y contactos son la misma entidad `Persona`; lo que cambia es qué extensiones tiene.
2. **Mismos nombres en 60 y 11.** La BD, los esquemas y los objetos se llaman igual en ambos servidores, para que el código del portal sea idéntico en los dos.
3. **Los portales conservan sus ids locales.** Tablas como `Usuario` o `Cliente` de EP360 pasan a ser **extensión 1 a 1** con un `idPersona`/`idCuenta` global. Así no se rompen las FK existentes (`Requerimiento`, `Recibo`, `Bitacora_Accion`, etc.).
4. **Credenciales separadas** en el esquema `seg`, solo accesibles por SP. Se replican al 11 (red interna), pero ningún portal del 11 las expone a internet.
5. Español, borrado lógico (`activo`), solo stored procedures, Controller → Service → DAL.
6. **Sin FK entre bases.** La integridad portal↔global se valida en el SP y con la tabla de equivalencias.

## 3. Esquemas

| Esquema | Contenido | ¿Se replica al 11? |
|---|---|---|
| `dir` | Personas, cuentas, grupos, contactos, catálogos | Sí (puede filtrarse, ver 8) |
| `seg` | Credenciales externas, invitaciones, bloqueos | Sí (el 11 es solo interno) |
| `sync` | Sincronización con AD, equivalencias de migración, bitácora de cambios | Solo `sync.BitacoraCambio` si se necesita |

## 4. Entidades y columnas

Solo lo que es **verdaderamente común**. Tipos tentativos.

### 4.1 `dir.Persona`
| Columna | Tipo | Notas |
|---|---|---|
| idPersona | INT IDENTITY PK | Id global. |
| tipoPersona | NVARCHAR(20) | `AD`, `Externo`, `Contacto` (CHECK). Cambia de `Contacto` a `Externo` al crearle credencial. |
| nombreCompleto | NVARCHAR(150) | |
| correo | NVARCHAR(200) NULL | Único filtrado entre activos con correo. Hay contactos de Balance sin correo (solo teléfono). |
| telefono | NVARCHAR(40) NULL | Principal. |
| extension | NVARCHAR(20) NULL | |
| puesto | NVARCHAR(120) NULL | |
| idDepartamento | INT NULL | FK `dir.Departamento` (internos). |
| idSucursal | INT NULL | FK `dir.Sucursal` (internos). |
| activo | BIT | Borrado lógico. |
| fechaCreacion / fechaModificacion | DATETIME | |
| idPersonaModifico | INT NULL | Quién hizo el último cambio. |

### 4.2 `dir.UsuarioAD` (1:1 con Persona)
| Columna | Tipo | Notas |
|---|---|---|
| idPersona | INT PK, FK | |
| objectSid | VARBINARY(85) | **Único. Identidad estable** (ningún portal lo guarda hoy). |
| samAccountName | NVARCHAR(100) | Único, comparación sin mayúsculas. Es lo que usan hoy los 4 portales. |
| userPrincipalName | NVARCHAR(200) NULL | |
| correoAD | NVARCHAR(200) NULL | Atributo `mail` real (hoy se fabrica `sam@eplogistics.com`). |
| habilitadoAD | BIT | Refleja `Enabled` en AD. |
| fechaUltimaSincronizacion | DATETIME | |

### 4.3 `seg.CredencialExterna` (1:1 con Persona, solo si ya es usuario externo)
| Columna | Tipo | Notas |
|---|---|---|
| idPersona | INT PK, FK | |
| passwordHash | NVARCHAR(256) NULL | PBKDF2 como hoy en EP360. NULL mientras está invitado. |
| estado | NVARCHAR(20) | `Invitado`, `Activo`, `Bloqueado`, `Desactivado`. |
| tokenHash | NVARCHAR(128) NULL | Hash del token de invitación o de restablecimiento; nunca el token. |
| fechaExpiraToken | DATETIME NULL | |
| debeCambiarPassword | BIT | |
| fechaCambioPassword | DATETIME NULL | |
| intentosFallidos | INT | |
| bloqueadoHasta | DATETIME NULL | |
| fechaCreacion / fechaModificacion | DATETIME | |

El historial de accesos (`UltimoAcceso`, bitácora de inicios de sesión) **se queda en cada portal**: el espejo es de solo lectura y no puede registrar logins.

### 4.4 `dir.GrupoCuenta`
`idGrupoCuenta` PK, `folio` NVARCHAR(30) (CGRP-…), `nombre` NVARCHAR(180), `activo`, fechas.
Une `GrupoCliente` (EP360) y `GruposClientesDirectorio` (Balance).

### 4.5 `dir.Cuenta`
| Columna | Tipo | Notas |
|---|---|---|
| idCuenta | INT IDENTITY PK | |
| folio | NVARCHAR(30) NULL | CCLI-… (Balance). |
| codigoCuenta | NVARCHAR(60) NULL | |
| nombreComercial | NVARCHAR(180) | |
| razonSocial | NVARCHAR(180) NULL | De EP360. |
| rfc | NVARCHAR(20) NULL | De EP360. |
| idGrupoCuenta | INT NULL | FK. |
| idCiudad | INT NULL | FK `dir.Ciudad`. |
| idSucursal | INT NULL | FK `dir.Sucursal`. |
| activo, fechas | | |

**No va en la global (extensión por portal):**
- EP360: `autorizacionNocturna`, `horaInicioOperativa`, `horaFinOperativa`, `ClienteEtiqueta`, `ClienteMapeoImportacion`.
- Balance: `Moneda`, `Periodicidad`, `NotasFacturacion`, `FacturaEstadosUnidos`, `FacturaMexico`, `IconoCuenta`, contratos y movimientos.

### 4.6 `dir.PersonaCuenta` (contacto de cuenta)
Relación muchos a muchos: una persona puede ser contacto de varias cuentas.
| Columna | Tipo | Notas |
|---|---|---|
| idPersonaCuenta | INT IDENTITY PK | |
| idPersona | INT FK | |
| idCuenta | INT FK | |
| categoria | NVARCHAR(30) NULL | Ventas, etc. (Balance). |
| puestoEnCuenta | NVARCHAR(120) NULL | |
| activo, fechas | | |

Para un usuario externo, esta tabla define **a qué cuentas pertenece**. El alcance por grupo (ver todas las cuentas del grupo, como hoy en EP360) se resuelve por `Cuenta.idGrupoCuenta`.

### 4.7 `dir.PersonaMedioContacto`
Medios adicionales de una persona (Balance guarda cada teléfono o correo como una fila):
`idMedio`, `idPersona`, `tipo` (`Telefono`/`Correo`), `categoria`, `valor`, `extension`, `activo`.

### 4.8 Catálogos
- `dir.Departamento` (`idDepartamento`, `nombre`, `activo`): se alimenta desde AD.
- `dir.Sucursal` (`idSucursal`, `nombre` único, `activo`): se alimenta desde AD.
- `dir.Ciudad` (`idCiudad`, `nombre`, `pais` MX/US, `activo`).
- `dir.Portal` (`idPortal`, `clave`, `nombre`): EP360, Balance, HelpDesk, Intranet, futuros.

**Lo que NO es catálogo global:** `Rol`, `nivelAcceso`, `Permiso`, `Modulo`, `UsuarioTipoMovimiento`, `Categorias_Responsables`. Son de cada portal.

### 4.9 `sync.*`
- `sync.EquivalenciaEntidad`: `idPortal`, `entidad` (`Persona`/`Cuenta`/`Grupo`), `idLocal`, `idGlobal`, `metodo` (`Automatica`/`Manual`), `revisado` BIT. Único por (`idPortal`, `entidad`, `idLocal`).
- `sync.SincronizacionAD`: corrida, fecha, altas, bajas, cambios, errores.
- `sync.BitacoraCambio`: `entidad`, `idEntidad`, `accion`, `detalle`, `idPersonaModifico`, `fecha`. Hoy ninguna tabla de directorio audita quién la cambió.

## 5. Qué conserva cada portal (extensión 1 a 1)

| Portal | Se queda local | Cambio |
|---|---|---|
| EP360 (`PortalEP`) | `Usuario` (idUsuario, `idPersona`, `idRol`, …), `Cliente` (idCliente, `idCuenta`, SLA), `Rol`, `UsuarioTipoMovimiento`, `ClienteEtiqueta`, `ClienteMapeoImportacion`, `Caja`, `Chofer`, bitácoras | `Usuario` pierde nombre/correo/hash/AD; `Cliente` pierde nombre/RFC/grupo/ciudad; `ContactoExterno`, `GrupoCliente`, `Departamento`, `Sucursal` y `Ciudad` salen o pasan a leer la global |
| Balance | `Roles`, `Permisos`, `UsuarioPermisos`, `RolPermisosDefault`, `Modulos`, contratos y movimientos por cuenta, `LogsAuditoria` | `Usuarios` y `ClientesDirectorio`, `Contactos…` y `Grupos…` pasan a la global |
| HelpDesk | `Roles`, `Categorias_Responsables`, `Tickets` | `Usuarios` queda como extensión (`idPersona`, `RolID`); `Planta` y `Departamento` salen |
| Intranet | `Tools`, `DepartmentBanners`, `Avisos`, `NovedadesImagenes` | Hoy no tiene tabla de usuarios; lee AD en vivo. Pasa a leer `dir.vw_UsuarioAD` |

Permisos por portal: cada portal tendrá su propia tabla con el `idPersona` global y su rol/permisos. **Un usuario externo necesita fila en el portal** para entrar (autenticar en la global no basta para autorizar).

## 6. Contrato de lectura y escritura

Convención: `dir.vw_*` y `dir.sp_*` en español, solo SP para escribir.

**Lectura (vistas, locales en 60 y 11)**
`dir.vw_Persona`, `dir.vw_UsuarioAD`, `dir.vw_Cuenta`, `dir.vw_GrupoCuenta`, `dir.vw_PersonaCuenta`, `dir.vw_Departamento`, `dir.vw_Sucursal`, `dir.vw_Ciudad`.
Las vistas solo exponen filas activas, salvo las pensadas para administración.

**Escritura (solo maestro 60)**
`dir.sp_CrearPersona`, `dir.sp_ActualizarPersona`, `dir.sp_DesactivarPersona`, `dir.sp_VincularPersonaCuenta`, `dir.sp_CrearCuenta`, `dir.sp_ActualizarCuenta`, `dir.sp_CrearGrupoCuenta`, `sync.sp_SincronizarUsuarioAD`.

**Credenciales (solo 60, solo SP)**
- `seg.sp_InvitarUsuarioExterno`: convierte un contacto en externo (crea credencial `Invitado`).
- `seg.sp_ActivarCredencial`: consume el token y fija la contraseña.
- `seg.sp_ObtenerCredencialPorCorreo`: devuelve hash, estado y bloqueo. El hash se compara en C# como hoy en EP360.
- `seg.sp_RegistrarIntentoFallido`, `seg.sp_RestablecerPassword`, `seg.sp_DesactivarCredencial`.

**Desde un servidor espejo (11):** los SP de escritura no existen allí. Las altas desde portales del 11 llaman al maestro (SP de alta hacia el 60, fase 6).

## 7. Seguridad (apps públicas en el 60)

- Un login SQL/pool por portal. En la global solo recibe `SELECT` sobre las vistas `dir.vw_*` y `EXECUTE` sobre los SP que le tocan. **Nada de `db_owner`.**
- El login de los portales públicos no puede leer tablas de `dir` directamente, solo vistas.
- `seg` solo se ejecuta por SP, y solo el login del servicio de autenticación.
- Una persona sin fila activa en el portal no entra, aunque su credencial sea válida.
- Bloqueo por usuario e IP (patrón ya existente en EP360, `LimitadorIntentosLoginHelper`) se queda en el portal. El bloqueo por usuario global es `seg.CredencialExterna.bloqueadoHasta`.
- Fase posterior: MFA, que cabe como columnas/tabla nuevas en `seg`.

## 8. Replicación al 11

- **Decidido:** se replica **todo** al 11: `dir`, `seg` (con hashes) y `sync`. El 11 es siempre local y el 60 es el público; la copia existe solo para bajar la latencia de lectura.
- Los logins de externos siguen yendo al 60 porque ahí corren las apps públicas. Las altas, invitaciones y cambios de contraseña se escriben solo en el 60.
- Consecuencia de seguridad: el 11 guarda hashes y datos de contactos, así que necesita las mismas restricciones de acceso que el 60 (logins mínimos, sin `db_owner` para los portales, sin exposición a internet).
- Consecuencia: el 11 tiene nombres, correos y teléfonos de contactos de clientes, aunque no tenga app pública. Debe tener el mismo nivel de acceso restringido que el 60.
- Como los ids solo se generan en el 60, no hay choques.

### 8.1 Bloqueo confirmado: ambos servidores son SQL Server Express

Verificado el 2026-10-02 con consulta de solo lectura: el 60 (`EPL1-EP360SERVER\EP360`) y el 11 (`EPL1-APPSERVER01\SQLEXPRESS`) son **Express Edition 2022**.
Express **no puede ser Publicador ni Distribuidor** de replicación transaccional (solo Suscriptor), y no trae SQL Agent. La replicación transaccional 60 → 11 del plan original **no es viable tal cual**.

| Opción | Cómo funciona | Pros / contras |
|---|---|---|
| A. Sincronización propia (recomendada) | Servicio o tarea programada de Windows que empuja cambios del 60 al 11 en lotes, usando Change Tracking (disponible en Express) o `rowversion`. Los portales siguen leyendo local, sin linked servers. El empuje usa conexión directa entre servidores, solo en la tarea. | Funciona en Express; retraso de segundos o minutos; hay que construir y monitorear la tarea. |
| B. Subir a Standard el 60 | Licencia Standard en el 60 (Publicador), el 11 puede seguir Express como Suscriptor. | Replicación nativa; cuesta licencia. |
| C. Backup/restore periódico | Respaldo del 60 restaurado en el 11 en modo `STANDBY`. | Simple, pero el 11 queda desconectado mientras restaura y el retraso es mayor. |

Pendiente de decidir (fase 6). No bloquea la fase 3: construir la global en el 60 no depende de la replicación.

## 9. Migración (orden propuesto)

0. **Sandbox EP360:** copia del código de EP360 + copia de la BD `PortalEP` (nombre propio, p. ej. `PortalEP_Global`), apuntando a la global. Todas las modificaciones se prueban aquí; EP360 de producción no se toca hasta validar.
1. Crear la global vacía en el 60 (esquemas, tablas, vistas, SP, login).
2. **Piloto:** usuarios de AD → `dir.Persona` + `dir.UsuarioAD` leyendo AD; Intranet los lee en solo lectura (no tiene tabla propia que migrar).
3. Poblar `sync.EquivalenciaEntidad` con los usuarios de AD de EP360, Balance y HelpDesk por `samAccountName`. Los que no cruzan quedan para revisión manual.
4. EP360: `Usuario` → extensión con `idPersona`; externos → `Persona` + `seg.CredencialExterna`.
5. Cuentas/grupos/contactos: unir EP360 con Balance por nombre normalizado + revisión manual (ver 10).
6. Balance, HelpDesk, y después portales nuevos.
7. Sincronización de la global completa (`dir`, `seg`, `sync`) al 11 (ver 8.1: la replicación transaccional no es viable en Express) y SP de altas hacia el maestro.
8. App web de administración. Su núcleo (altas, invitaciones, desactivación de externos) debe estar **antes** de publicar Balance.

## 10. Riesgos y puntos abiertos

| # | Riesgo / pendiente | Mitigación |
|---|---|---|
| 1 | No hay clave estable de cuenta; EP360 y Balance la identifican por nombre. | Normalizar (mayúsculas, sin acentos, sin puntuación), emparejar y revisar a mano; guardar el resultado en `sync.EquivalenciaEntidad`. |
| 2 | `samAccountName` puede cambiar; hoy es el único vínculo. | Guardar `objectSid` y usarlo como identidad; `samAccountName` solo para búsqueda. |
| 3 | Correos fabricados `sam@eplogistics.com`. | Tomar `mail`/UPN reales de AD en la sincronización. |
| 4 | Mismo `samAccountName` o correo con varias filas entre portales. | Diagnóstico antes de migrar (fase 1): detectar duplicados y conflictos. |
| 5 | Sin FK entre bases (portal ↔ global). | Validar en SP; job de verificación de huérfanos. |
| 6 | Un contacto sin correo (Balance) no puede hacerse usuario externo. | El SP de invitación exige correo. |
| 7 | `nombreUsuarioAD` de EP360 sin UNIQUE; HelpDesk sin DDL de `Usuarios`/`Roles` en el repo. | Verificar en el servidor con `INFORMATION_SCHEMA` (fase 1). |
| 8 | Balance está en LocalDB; no tiene servidor definido. | Decidir antes de migrar (probablemente el 60, ya que será público). |
| 9 | `EntrarComo(idUsuario)` de Balance (acceso sin contraseña para cuentas locales). | Retirarlo antes de publicar. |
| 10 | Quién administra qué: ¿la app de administración ve permisos de todos los portales? | Decidir en fase 7; opción: tabla `dir.PersonaPortal` (qué portales tiene cada persona) sin permisos finos. |
| 11 | EPSTARTPOINT / "Directorio" aparece en los `CLAUDE.md` y no está en los 4 repos. | Revisar si tiene su propio inventario de cuentas o usuarios. |

## 11. Qué se necesita de ti para pasar a la fase 3 (scripts)

1. Revisar este diseño y decir qué cambia.
2. Confirmar edición de SQL Server en el 60 y el 11 (para replicación transaccional).
3. Correr en HelpDesk `INFORMATION_SCHEMA.COLUMNS` de `Usuarios` y `Roles` y pasármelo.
4. Decidir el punto 10 (alcance de la app de administración). El punto 8 ya está decidido.

Los scripts se generarán numerados en `Database/` y tú los corres; yo avisaré cuáles.
