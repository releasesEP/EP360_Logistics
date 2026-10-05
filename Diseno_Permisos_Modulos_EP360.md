# Diseño: permisos por módulo en EP360 (sin roles) y departamentos desde la global

Fecha: 2026-10-05. Estado: **propuesta para revisión**, sin cambios aplicados.
Base: análisis de `Services/`, `Controllers/`, `Views/` y la BD de EP360 (copia `EP360_Traceability`) + el modelo de EP360 Balance
(`Permisos` + `Modulos` + `UsuarioPermisos`, acceso directo por usuario).

## 1. Qué hace hoy el rol y por qué se puede reemplazar

El rol de EP360 (20 roles) guarda **tres cosas**: el **departamento** del usuario, su **nivel** (Agente/Gerente/Administrador/Dirección)
y su **tipo** (Interno/Cliente). El análisis muestra que:

- Casi toda la autorización está en C# (≈25 servicios con copias de `EsRolTrafico`, `EsRolAlmacen`, `EsRolAdministradorIT`...). La BD casi no autoriza.
- Lo que decide es el **nombre del departamento** (literal: "Tráfico", "Almacén", "Aduanas", "Brokerage", "Customer Service", "Sistemas").
  Una vez pasado ese filtro, **Agente = Gerente**. El nivel solo importa en 3 sitios: dashboard personal vs. agregado, reportes gerenciales y administrador.
- Hay ~15 **capacidades** distintas. Son la base de los permisos.
- El tipo (Interno/Cliente) ya viene de la persona en la global (AD = interno, Externo = cliente).

## Decisiones ya tomadas (2026-10-05)

- Permisos **sí/no por módulo, como Balance** (no un nivel por módulo ni un nivel único por usuario).
- Se empieza por **departamentos y módulos de acceso**.
- **Almacén y Warehouse son un solo permiso** (`Almacen.Operar`). Consecuencia a vigilar: las 7 personas de Warehouse ganarían operaciones en Requerimientos y Recibo MX que hoy no tienen; el script masivo lo deja a la vista para ajustarlo persona por persona.
- **Coordinador = permiso `Requerimientos.Coordinar` + departamento de AD igual al coordinador de la plantilla** (se conserva el coordinador por plantilla).
- El catálogo de permisos de la sección 3 se **revisa en este documento antes de escribir los scripts**.

## 2. Modelo propuesto (como Balance)

Tablas en EP360 (`Permiso`, `UsuarioPermiso`, y `ModuloEP360` para armar el menú), aditivas y reversibles:

| Tabla | Contenido |
|---|---|
| `Permiso` | `idPermiso`, `clave` (única), `nombre`, `descripcion`, `modulo`, `activo` |
| `UsuarioPermiso` | `idUsuario` (local de EP360), `idPermiso`, `fechaAsignacion`, `idUsuarioAsigno` — PK compuesta; borrado físico como Balance, la bitácora guarda quién y cuándo |
| `ModuloEP360` | `clave`, `nombre`, `controlador`, `idPermisoRequerido` — define qué entradas del menú ve cada quien |

El permiso es **sí/no por usuario**. No hay roles, ni plantilla por rol en operación (solo en el script masivo de migración y,
si se quiere, como atajo al dar acceso a alguien nuevo).

## 2.1 Perfiles predeterminados (plantillas, no roles)

Pedido del 2026-10-05: tener "roles predeterminados" para no marcar permiso por permiso al dar acceso. Se conservan **los 20 roles de hoy como plantillas**
(`PerfilPermisos` + `PerfilPermisoDetalle`, script 102), igual que `RolPermisosDefault` en Balance:

- **Dar acceso a EP360:** eliges persona + perfil; las casillas salen marcadas y se pueden ajustar. Se guardan los **permisos del usuario**, no el perfil.
- **Aplicar perfil** a un usuario ya existente: **agrega** lo que falte, nunca quita.
- **La autorización nunca lee un perfil**, solo `UsuarioPermiso`. Cambiar un perfil no modifica a quienes ya lo recibieron.
- Los perfiles son datos editables (en la fase 2 habrá pantalla para crear, renombrar y ajustar perfiles). Los permisos iniciales de cada perfil salen de las mismas reglas
  que el script masivo (verificado: 0 diferencias contra lo ya asignado a los usuarios).

## 3. Catálogo de permisos (propuesto)

| Clave | Reemplaza (hoy) | Se usa en |
|---|---|---|
| `Admin.Sistema` | Administrador IT: Rol Sistemas+Administrador **o** grupo AD `ep360admins` | ~85 métodos de 14 servicios: catálogos, plantillas, usuarios, seguridad, reactivar |
| `Requerimientos.Coordinar` | Coordinador del flujo (Customer Service por defecto, o el departamento coordinador de la plantilla) | avanzar/retroceder estatus, tomar, cerrar, reasignar CSR/caja/chofer, prioridad, orden, ampliaciones |
| `Trafico.Operar` | dept Tráfico + Agente/Gerente | cajas, choferes, notas de salida/llegada, campos dinámicos, crear Recibo MX, asignar en Push |
| `Almacen.Operar` | dept Almacén (+Warehouse en Push/Recibo USA/Preview) + Agente/Gerente | Recibos, Push, Recibo USA, Discrepancias, Inspección Agrícola, Preview |
| `Aduanas.Operar` | dept Aduanas + Agente/Gerente | Push (extracción, documentación aduanal), documentos de requerimiento |
| `Brokerage.Operar` | dept Brokerage + Agente/Gerente | Recibo USA, Push, Preview |
| `CustomerService.Operar` | dept Customer Service + Agente/Gerente | Recibo MX (ver, mensajes, PDF completo) |
| `Reportes.Movimientos` | Gerente/Admin de Almacén, Warehouse, Brokerage, Tráfico o Aduanas | reportes Push / Recibo USA |
| `Reportes.Requerimientos` | Gerente/Admin de Customer Service, Tráfico, Almacén o Aduanas | reporte de requerimientos |
| `Reportes.Recibos` | Gerente/Admin de Customer Service, Tráfico o Almacén | reporte de Recibos MX |
| `Reportes.Almacen` | Gerente/Admin de Almacén | Discrepancias e Inspección Agrícola |
| `Reportes.Cliente` | rol "Cliente Reportes" | reportes acotados a su grupo de cuentas |
| `Dashboard.VerTodo` | Gerente, Dirección o Admin (no Agente) | home con el agregado y "carga por agente" |
| `Almacen.ElPaso` *(no es permiso)* | sucursal = EL PASO | **sigue siendo una condición**: viene de `dir.Persona.idSucursal` (global) |

El **cerco por grupo de cuentas** del usuario externo no es un permiso: lo da su tipo (Externo) y sus cuentas en la global.
La **restricción por tipo de movimiento** (`UsuarioTipoMovimiento`) y la de **sucursal** se quedan igual.

## 4. Departamentos desde la global

Mismo método que ya se usó con sucursales: `Departamento` pasa a ser una **vista** con las mismas columnas que lee `EP360_Logistics.dir.Departamento`;
el id local de EP360 se conserva (8 tablas del flujo lo referencian por FK: `EstatusBase`, `PlantillaFlujoEstatus`, `PlantillaFlujo.idDepartamentoCoordinador`,
3 bitácoras de estatus, `RequerimientoMensaje`) y se liga al id global.

- La vista `Usuario` expone `idDepartamento` leyendo `dir.Persona.idDepartamento` (el de AD). **El departamento del usuario deja de salir del rol.**
- Los duplicados (`SISTEMAS` ×3, `Customer Service` ×3, `CSR` vacío, etc.) se **fusionan** en un script de mapeo con revisión; las FK se re-apuntan al id local canónico.
- Los nombres literales del código ("Tráfico", "Almacén"...) dejan de ser el mecanismo de autorización (lo reemplazan los permisos); donde aún se usen
  (coordinador de plantilla, bandeja, notificaciones, origen de mensajes, departamento responsable del retroceso) se leen del **departamento del usuario en la global**.
- **Riesgo conocido:** un usuario sin departamento en AD queda sin bandeja/notificaciones por departamento (los permisos sí le siguen sirviendo).

## 5. Fases (cada una se prueba antes de la siguiente; ninguna se aplica sola)

1. **Aditiva, sin cambiar comportamiento** (scripts SQL): tablas `Permiso`/`UsuarioPermiso`/`ModuloEP360`, semilla del catálogo, **script masivo roles → permisos**,
   mapeo y vista de departamentos con `Usuario.idDepartamento`, y un **script de equivalencia** que compara, para cada usuario, lo que decidía el rol con lo que decidirán los permisos (diferencias = 0).
2. **Código**: un servicio central (`AccesoService`) que contesta las mismas preguntas desde los permisos; los ~25 `EsRol*` duplicados pasan a delegar en él; ficha de usuario con casillas de permisos;
   "Dar acceso" sin rol. Prueba de equivalencia antes y después.
3. **Limpieza**: quitar CRUD de Roles y Departamentos (rutas, vistas, SPs, entradas de `.csproj`, textos), retirar `UsuarioEP360.idRol` y la tabla `Rol`.

## 6. Pendiente después de esto (ya acordado, otras piezas)

- Contactos de cuentas (`ContactoExterno`) en la global; EP360 conserva solo los grupos de notificación por evento.
- Panel de métricas de login por portal y servicios conectados en la app de la global.
- Invitación por correo a usuarios externos.
