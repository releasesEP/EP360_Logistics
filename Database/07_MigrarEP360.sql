/* =====================================================================
   07_MigrarEP360.sql
   BD global: EP360_Logistics   (servidor 60, maestro)
   REQUIERE haber corrido antes: 01 a 06, y haber sincronizado AD (usuarios de AD ya cargados).
   ORIGEN (solo se LEE): EP360_Traceability  (copia de pruebas de EP360, mismo servidor)

   Migra a la global lo que EP360 tiene de directorio y registra en sync.EquivalenciaEntidad
   (portal EP360) el id local de EP360 <-> el id global, para que EP360 pueda apuntar a la global
   sin perder su historial.

     1. Ciudades     -> dir.Ciudad              (equivalencia entidad Ciudad)
     2. Sucursales   -> dir.Sucursal            (equivalencia entidad Sucursal)
     3. Grupos       -> dir.GrupoCuenta         (equivalencia entidad Grupo)
     4. Cuentas      -> dir.Cuenta              (equivalencia entidad Cuenta)
     5. Usuarios     -> dir.Persona             (equivalencia entidad Persona)
          a) usuarios de AD que cruzan por samAccountName con dir.UsuarioAD: solo equivalencia
          b) usuarios de AD que YA NO estan activos/ni existen en la global (ex-empleados o cuentas
             deshabilitadas): se crean como Persona tipo Contacto INACTIVA, para que el historial de
             EP360 conserve su nombre; no pueden entrar
          c) usuarios externos de clientes: Persona tipo Externo + seg.CredencialExterna (el hash de
             contrasena se copia tal cual; mismo PBKDF2 de EP360) + vinculo a su cuenta
          d) usuarios internos sin AD (ej. Sistema EP360 Go): Persona tipo Contacto
     6. Contactos externos de cuenta (ContactoExterno) -> vinculo Persona <-> Cuenta (si el correo ya
        es una persona de la global, se reutiliza; si no, se crea como Contacto)

   NO migra: roles, departamentos de EP360, UsuarioTipoMovimiento, ClienteEtiqueta,
   ClienteMapeoImportacion, configuracion de SLA, grupos de contacto. Eso se queda en EP360.

   Reglas de esta migracion:
     * Es IDEMPOTENTE: usa sync.EquivalenciaEntidad para no repetir nada. Se puede correr otra vez.
     * Todo ocurre en UNA transaccion: si algo falla, no queda nada a medias.
     * No modifica EP360_Traceability.
     * Las cuentas con nombre repetido en EP360 (ej. ROYAL DYNAMIC SOLUTIONS, LLC) se migran como
       cuentas SEPARADAS y quedan con revisado = 0 en la equivalencia, para revision manual.
     * Todo lo que no es un cruce 100% seguro queda con revisado = 0 (ver reporte al final).

   Quien lo corre: el dueno del proyecto, en SSMS, conectado al 60.
   ===================================================================== */

USE EP360_Logistics;
GO

/* La tabla de equivalencias ahora tambien guarda ciudades y sucursales. */
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_EquivalenciaEntidad_entidad' AND definition LIKE N'%Ciudad%')
BEGIN
    ALTER TABLE sync.EquivalenciaEntidad DROP CONSTRAINT CK_EquivalenciaEntidad_entidad;
    ALTER TABLE sync.EquivalenciaEntidad ADD CONSTRAINT CK_EquivalenciaEntidad_entidad
        CHECK (entidad IN (N'Persona', N'Cuenta', N'Grupo', N'Ciudad', N'Sucursal'));
END
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @idPortal INT = (SELECT idPortal FROM dir.Portal WHERE clave = N'EP360');
IF @idPortal IS NULL THROW 50041, N'No existe el portal EP360 en dir.Portal (script 01).', 1;
IF DB_ID(N'EP360_Traceability') IS NULL THROW 50042, N'No existe la base de origen EP360_Traceability.', 1;

BEGIN TRY
BEGIN TRAN;

/* ---------------------------------------------------------------------
   1. CIUDADES
   --------------------------------------------------------------------- */
INSERT INTO dir.Ciudad (nombre, pais, activo)
SELECT c.nombre, c.pais, c.activo
FROM EP360_Traceability.dbo.Ciudad c
WHERE NOT EXISTS (SELECT 1 FROM dir.Ciudad g WHERE g.nombre = c.nombre AND g.pais = c.pais);

INSERT INTO sync.EquivalenciaEntidad (idPortal, entidad, idLocal, idGlobal, nombreOrigen, metodo, revisado)
SELECT @idPortal, N'Ciudad', c.idCiudad,
       (SELECT TOP (1) g.idCiudad FROM dir.Ciudad g WHERE g.nombre = c.nombre AND g.pais = c.pais ORDER BY g.activo DESC, g.idCiudad),
       c.nombre, N'Automatica', 1
FROM EP360_Traceability.dbo.Ciudad c
WHERE NOT EXISTS (SELECT 1 FROM sync.EquivalenciaEntidad e WHERE e.idPortal = @idPortal AND e.entidad = N'Ciudad' AND e.idLocal = c.idCiudad);

/* ---------------------------------------------------------------------
   2. SUCURSALES (la global ya trae las que vienen de AD; se cruza por nombre)
   --------------------------------------------------------------------- */
INSERT INTO dir.Sucursal (nombre, activo)
SELECT s.nombre, s.activo
FROM EP360_Traceability.dbo.Sucursal s
WHERE NOT EXISTS (SELECT 1 FROM dir.Sucursal g WHERE g.nombre = s.nombre);

INSERT INTO sync.EquivalenciaEntidad (idPortal, entidad, idLocal, idGlobal, nombreOrigen, metodo, revisado)
SELECT @idPortal, N'Sucursal', s.idSucursal,
       (SELECT TOP (1) g.idSucursal FROM dir.Sucursal g WHERE g.nombre = s.nombre ORDER BY g.activo DESC, g.idSucursal),
       s.nombre, N'Automatica', 1
FROM EP360_Traceability.dbo.Sucursal s
WHERE NOT EXISTS (SELECT 1 FROM sync.EquivalenciaEntidad e WHERE e.idPortal = @idPortal AND e.entidad = N'Sucursal' AND e.idLocal = s.idSucursal);

/* ---------------------------------------------------------------------
   3. GRUPOS DE CUENTAS
   a) si ya hay un grupo ACTIVO con el mismo nombre en la global, se reutiliza (solo equivalencia)
   b) los demas se crean con folio CGRP-yyMMdd-n
   --------------------------------------------------------------------- */
INSERT INTO sync.EquivalenciaEntidad (idPortal, entidad, idLocal, idGlobal, nombreOrigen, metodo, revisado)
SELECT @idPortal, N'Grupo', g.idGrupoCliente,
       (SELECT TOP (1) x.idGrupoCuenta FROM dir.GrupoCuenta x WHERE x.nombre = g.nombre AND x.activo = 1 ORDER BY x.idGrupoCuenta),
       g.nombre, N'Automatica', 0
FROM EP360_Traceability.dbo.GrupoCliente g
WHERE NOT EXISTS (SELECT 1 FROM sync.EquivalenciaEntidad e WHERE e.idPortal = @idPortal AND e.entidad = N'Grupo' AND e.idLocal = g.idGrupoCliente)
  AND EXISTS (SELECT 1 FROM dir.GrupoCuenta x WHERE x.nombre = g.nombre AND x.activo = 1);

DECLARE @prefGrupo NVARCHAR(20) = N'CGRP-' + FORMAT(GETDATE(), 'yyMMdd') + N'-';
DECLARE @baseGrupo INT = (SELECT COUNT(*) FROM dir.GrupoCuenta WHERE folio LIKE @prefGrupo + N'%');
DECLARE @gruposNuevos TABLE (idLocal INT PRIMARY KEY, nombre NVARCHAR(180) COLLATE DATABASE_DEFAULT, activo BIT, n INT);
DECLARE @mapaGrupos   TABLE (idLocal INT PRIMARY KEY, idGlobal INT);

INSERT INTO @gruposNuevos (idLocal, nombre, activo, n)
SELECT g.idGrupoCliente, LTRIM(RTRIM(g.nombre)), g.activo, ROW_NUMBER() OVER (ORDER BY g.idGrupoCliente)
FROM EP360_Traceability.dbo.GrupoCliente g
WHERE NOT EXISTS (SELECT 1 FROM sync.EquivalenciaEntidad e WHERE e.idPortal = @idPortal AND e.entidad = N'Grupo' AND e.idLocal = g.idGrupoCliente);

MERGE dir.GrupoCuenta AS t
USING @gruposNuevos AS s ON 1 = 0
WHEN NOT MATCHED THEN
    INSERT (folio, nombre, activo) VALUES (@prefGrupo + CAST(@baseGrupo + s.n AS NVARCHAR(10)), s.nombre, s.activo)
OUTPUT s.idLocal, inserted.idGrupoCuenta INTO @mapaGrupos (idLocal, idGlobal);

INSERT INTO sync.EquivalenciaEntidad (idPortal, entidad, idLocal, idGlobal, nombreOrigen, metodo, revisado)
SELECT @idPortal, N'Grupo', m.idLocal, m.idGlobal, n.nombre, N'Automatica', 1
FROM @mapaGrupos m JOIN @gruposNuevos n ON n.idLocal = m.idLocal;

/* ---------------------------------------------------------------------
   4. CUENTAS (Cliente de EP360). Los nombres repetidos se migran como cuentas separadas y
   quedan con revisado = 0. Folio CCLI-yyMMdd-n.
   --------------------------------------------------------------------- */
DECLARE @prefCuenta NVARCHAR(20) = N'CCLI-' + FORMAT(GETDATE(), 'yyMMdd') + N'-';
DECLARE @baseCuenta INT = (SELECT COUNT(*) FROM dir.Cuenta WHERE folio LIKE @prefCuenta + N'%');
DECLARE @cuentasNuevas TABLE
(
    idLocal INT PRIMARY KEY, nombre NVARCHAR(180) COLLATE DATABASE_DEFAULT, razonSocial NVARCHAR(180) COLLATE DATABASE_DEFAULT,
    rfc NVARCHAR(20) COLLATE DATABASE_DEFAULT, idGrupoCuenta INT, idCiudad INT, activo BIT, repetido BIT, n INT
);
DECLARE @mapaCuentas TABLE (idLocal INT PRIMARY KEY, idGlobal INT);

INSERT INTO @cuentasNuevas (idLocal, nombre, razonSocial, rfc, idGrupoCuenta, idCiudad, activo, repetido, n)
SELECT c.idCliente, LTRIM(RTRIM(c.nombreComercial)), NULLIF(LTRIM(RTRIM(c.razonSocial)), N''), NULLIF(LTRIM(RTRIM(c.rfc)), N''),
       eg.idGlobal, ec.idGlobal, c.activo,
       CASE WHEN COUNT(*) OVER (PARTITION BY UPPER(LTRIM(RTRIM(c.nombreComercial)))) > 1 THEN 1 ELSE 0 END,
       ROW_NUMBER() OVER (ORDER BY c.idCliente)
FROM EP360_Traceability.dbo.Cliente c
LEFT JOIN sync.EquivalenciaEntidad eg ON eg.idPortal = @idPortal AND eg.entidad = N'Grupo'  AND eg.idLocal = c.idGrupoCliente
LEFT JOIN sync.EquivalenciaEntidad ec ON ec.idPortal = @idPortal AND ec.entidad = N'Ciudad' AND ec.idLocal = c.idCiudad
WHERE NOT EXISTS (SELECT 1 FROM sync.EquivalenciaEntidad e WHERE e.idPortal = @idPortal AND e.entidad = N'Cuenta' AND e.idLocal = c.idCliente);

MERGE dir.Cuenta AS t
USING @cuentasNuevas AS s ON 1 = 0
WHEN NOT MATCHED THEN
    INSERT (folio, nombreComercial, razonSocial, rfc, idGrupoCuenta, idCiudad, activo)
    VALUES (@prefCuenta + CAST(@baseCuenta + s.n AS NVARCHAR(10)), s.nombre, s.razonSocial, s.rfc, s.idGrupoCuenta, s.idCiudad, s.activo)
OUTPUT s.idLocal, inserted.idCuenta INTO @mapaCuentas (idLocal, idGlobal);

INSERT INTO sync.EquivalenciaEntidad (idPortal, entidad, idLocal, idGlobal, nombreOrigen, metodo, revisado)
SELECT @idPortal, N'Cuenta', m.idLocal, m.idGlobal, n.nombre, N'Automatica', CASE WHEN n.repetido = 1 THEN 0 ELSE 1 END
FROM @mapaCuentas m JOIN @cuentasNuevas n ON n.idLocal = m.idLocal;

/* ---------------------------------------------------------------------
   5a. USUARIOS DE AD que cruzan con la global por samAccountName (solo equivalencia).
   --------------------------------------------------------------------- */
INSERT INTO sync.EquivalenciaEntidad (idPortal, entidad, idLocal, idGlobal, nombreOrigen, metodo, revisado)
SELECT @idPortal, N'Persona', u.idUsuario, g.idPersona, u.nombreUsuarioAD, N'Automatica', 1
FROM EP360_Traceability.dbo.Usuario u
JOIN dir.UsuarioAD g ON g.samAccountName = u.nombreUsuarioAD
WHERE u.nombreUsuarioAD IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM sync.EquivalenciaEntidad e WHERE e.idPortal = @idPortal AND e.entidad = N'Persona' AND e.idLocal = u.idUsuario);

/* ---------------------------------------------------------------------
   5b/5c/5d. USUARIOS RESTANTES (sin cruce con AD):
     - con nombreUsuarioAD pero sin persona en la global  -> Contacto INACTIVO (historial)
     - con cliente                                         -> Externo (+ credencial)
     - internos sin AD ni cliente                          -> Contacto
   Un usuario EXTERNO cuyo correo ya es una persona activa de la global NO se duplica: se reutiliza.
   --------------------------------------------------------------------- */
DECLARE @usuariosNuevos TABLE
(
    idLocal INT PRIMARY KEY, tipo NVARCHAR(20) COLLATE DATABASE_DEFAULT, nombre NVARCHAR(150) COLLATE DATABASE_DEFAULT,
    correo NVARCHAR(200) COLLATE DATABASE_DEFAULT, activo BIT, puesto NVARCHAR(120) COLLATE DATABASE_DEFAULT,
    passwordHash NVARCHAR(256) COLLATE DATABASE_DEFAULT, idCliente INT, idPersonaExistente INT, revisado BIT
);
DECLARE @mapaUsuarios TABLE (idLocal INT PRIMARY KEY, idGlobal INT);

INSERT INTO @usuariosNuevos (idLocal, tipo, nombre, correo, activo, puesto, passwordHash, idCliente, idPersonaExistente, revisado)
SELECT u.idUsuario,
       CASE WHEN u.nombreUsuarioAD IS NOT NULL THEN N'Contacto' WHEN u.idCliente IS NOT NULL THEN N'Externo' ELSE N'Contacto' END,
       LTRIM(RTRIM(u.nombreCompleto)),
       NULLIF(LTRIM(RTRIM(u.correo)), N''),
       CASE WHEN u.nombreUsuarioAD IS NOT NULL THEN 0 ELSE u.activo END,
       CASE WHEN u.nombreUsuarioAD IS NULL AND u.idCliente IS NULL THEN N'Cuenta interna sin AD (migrada de EP360)'
            WHEN u.nombreUsuarioAD IS NOT NULL THEN N'Ex-usuario interno (ya no activo en AD)' END,
       u.passwordHash, u.idCliente,
       CASE WHEN u.idCliente IS NOT NULL AND u.nombreUsuarioAD IS NULL
            THEN (SELECT TOP (1) p.idPersona FROM dir.Persona p WHERE p.correo = u.correo AND p.activo = 1 ORDER BY p.idPersona) END,
       0
FROM EP360_Traceability.dbo.Usuario u
WHERE NOT EXISTS (SELECT 1 FROM sync.EquivalenciaEntidad e WHERE e.idPortal = @idPortal AND e.entidad = N'Persona' AND e.idLocal = u.idUsuario);

/* Cuenta interna sin AD con un correo que ya usa otra persona activa: se migra sin correo (el correo es unico entre activos). */
UPDATE n SET n.correo = NULL
FROM @usuariosNuevos n
WHERE n.activo = 1 AND n.correo IS NOT NULL AND n.idPersonaExistente IS NULL
  AND EXISTS (SELECT 1 FROM dir.Persona p WHERE p.correo = n.correo AND p.activo = 1);

MERGE dir.Persona AS t
USING (SELECT * FROM @usuariosNuevos WHERE idPersonaExistente IS NULL) AS s ON 1 = 0
WHEN NOT MATCHED THEN
    INSERT (tipoPersona, nombreCompleto, correo, puesto, activo)
    VALUES (s.tipo, s.nombre, s.correo, s.puesto, s.activo)
OUTPUT s.idLocal, inserted.idPersona INTO @mapaUsuarios (idLocal, idGlobal);

INSERT INTO @mapaUsuarios (idLocal, idGlobal)
SELECT idLocal, idPersonaExistente FROM @usuariosNuevos WHERE idPersonaExistente IS NOT NULL;

INSERT INTO sync.EquivalenciaEntidad (idPortal, entidad, idLocal, idGlobal, nombreOrigen, metodo, revisado)
SELECT @idPortal, N'Persona', m.idLocal, m.idGlobal, n.nombre, N'Automatica', 0
FROM @mapaUsuarios m JOIN @usuariosNuevos n ON n.idLocal = m.idLocal;

/* Credenciales de los externos recien creados (no de los reutilizados). */
INSERT INTO seg.CredencialExterna (idPersona, passwordHash, estado)
SELECT m.idGlobal, n.passwordHash,
       CASE WHEN n.activo = 0 THEN N'Desactivado' WHEN n.passwordHash IS NOT NULL THEN N'Activo' ELSE N'Invitado' END
FROM @mapaUsuarios m
JOIN @usuariosNuevos n ON n.idLocal = m.idLocal
WHERE n.tipo = N'Externo' AND n.idPersonaExistente IS NULL
  AND NOT EXISTS (SELECT 1 FROM seg.CredencialExterna c WHERE c.idPersona = m.idGlobal);

/* Vinculo de cada externo con su cuenta. */
INSERT INTO dir.PersonaCuenta (idPersona, idCuenta)
SELECT DISTINCT ep.idGlobal, ecu.idGlobal
FROM EP360_Traceability.dbo.Usuario u
JOIN sync.EquivalenciaEntidad ep  ON ep.idPortal  = @idPortal AND ep.entidad  = N'Persona' AND ep.idLocal  = u.idUsuario
JOIN sync.EquivalenciaEntidad ecu ON ecu.idPortal = @idPortal AND ecu.entidad = N'Cuenta'  AND ecu.idLocal = u.idCliente
WHERE u.nombreUsuarioAD IS NULL AND u.idCliente IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dir.PersonaCuenta pc WHERE pc.idPersona = ep.idGlobal AND pc.idCuenta = ecu.idGlobal AND pc.activo = 1);

/* ---------------------------------------------------------------------
   6. CONTACTOS EXTERNOS DE CUENTA (ContactoExterno de EP360)
   Se reutiliza a la persona de la global con el mismo correo; si no existe, se crea como Contacto.
   --------------------------------------------------------------------- */
DECLARE @contactosNuevos TABLE (idLocal INT PRIMARY KEY, nombre NVARCHAR(150) COLLATE DATABASE_DEFAULT, correo NVARCHAR(200) COLLATE DATABASE_DEFAULT, activo BIT);

INSERT INTO @contactosNuevos (idLocal, nombre, correo, activo)
SELECT c.idContactoExterno, LTRIM(RTRIM(c.nombre)), LTRIM(RTRIM(c.correo)), c.activo
FROM EP360_Traceability.dbo.ContactoExterno c
WHERE NOT EXISTS (SELECT 1 FROM dir.Persona p WHERE p.correo = LTRIM(RTRIM(c.correo)));

INSERT INTO dir.Persona (tipoPersona, nombreCompleto, correo, activo)
SELECT N'Contacto', nombre, correo, activo FROM @contactosNuevos;

INSERT INTO dir.PersonaCuenta (idPersona, idCuenta)
SELECT DISTINCT p.idPersona, ecu.idGlobal
FROM EP360_Traceability.dbo.ContactoExterno c
JOIN dir.Persona p ON p.correo = LTRIM(RTRIM(c.correo))
JOIN sync.EquivalenciaEntidad ecu ON ecu.idPortal = @idPortal AND ecu.entidad = N'Cuenta' AND ecu.idLocal = c.idCliente
WHERE c.activo = 1
  AND NOT EXISTS (SELECT 1 FROM dir.PersonaCuenta pc WHERE pc.idPersona = p.idPersona AND pc.idCuenta = ecu.idGlobal AND pc.activo = 1);

COMMIT;
PRINT 'Migracion de EP360 terminada correctamente.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
GO

/* =====================================================================
   REPORTE (solo lectura): que se migro y que queda por revisar a mano.
   ===================================================================== */
DECLARE @p INT = (SELECT idPortal FROM dir.Portal WHERE clave = N'EP360');

SELECT entidad, COUNT(*) AS migrados, SUM(CASE WHEN revisado = 0 THEN 1 ELSE 0 END) AS porRevisar
FROM sync.EquivalenciaEntidad WHERE idPortal = @p GROUP BY entidad ORDER BY entidad;

SELECT N'Cuentas con nombre repetido (revisar)' AS aRevisar, e.idLocal AS idClienteEP360, e.idGlobal AS idCuentaGlobal, e.nombreOrigen
FROM sync.EquivalenciaEntidad e WHERE e.idPortal = @p AND e.entidad = N'Cuenta' AND e.revisado = 0 ORDER BY e.nombreOrigen;

SELECT N'Usuarios de EP360 migrados como persona nueva (revisar)' AS aRevisar, e.idLocal AS idUsuarioEP360, e.idGlobal AS idPersonaGlobal,
       p.tipoPersona, p.activo, p.nombreCompleto, p.correo
FROM sync.EquivalenciaEntidad e JOIN dir.Persona p ON p.idPersona = e.idGlobal
WHERE e.idPortal = @p AND e.entidad = N'Persona' AND e.revisado = 0 ORDER BY p.tipoPersona, p.nombreCompleto;

SELECT N'Usuarios de EP360 SIN equivalencia (deberia ser 0)' AS aRevisar, COUNT(*) AS n
FROM EP360_Traceability.dbo.Usuario u
WHERE NOT EXISTS (SELECT 1 FROM sync.EquivalenciaEntidad e WHERE e.idPortal = @p AND e.entidad = N'Persona' AND e.idLocal = u.idUsuario);
GO
