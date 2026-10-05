/* =====================================================================
   02_TablasNucleoDirectorio.sql
   BD global: EP360_Logistics   (servidor 60, maestro)
   Requiere haber corrido antes: 01_CrearBaseYEsquemas.sql

   Crea las tablas del nucleo del directorio (esquema dir):
     Catalogos : dir.Departamento, dir.Sucursal, dir.Ciudad
     Cuentas   : dir.GrupoCuenta, dir.Cuenta
     Personas  : dir.Persona, dir.UsuarioAD, dir.PersonaCuenta, dir.PersonaMedioContacto

   NO incluye todavia (scripts siguientes):
     - vistas dir.vw_* y stored procedures dir.sp_*   (script 03)
     - seg.CredencialExterna                           (script 04)
     - sync.EquivalenciaEntidad, sync.SincronizacionAD,
       sync.BitacoraCambio                             (script 05)

   Es IDEMPOTENTE: no borra ni modifica tablas que ya existan.
   No inserta datos (la carga desde AD / EP360 / Balance es posterior).

   Quien lo corre: el dueno del proyecto, en SSMS, conectado al 60.
   ===================================================================== */

USE EP360_Logistics;
GO

/* ---------------------------------------------------------------------
   Catalogos
   --------------------------------------------------------------------- */
IF OBJECT_ID(N'dir.Departamento', N'U') IS NULL
BEGIN
    CREATE TABLE dir.Departamento
    (
        idDepartamento    INT IDENTITY(1,1) NOT NULL,
        nombre            NVARCHAR(100)     NOT NULL,
        activo            BIT               NOT NULL CONSTRAINT DF_Departamento_activo DEFAULT (1),
        fechaCreacion     DATETIME          NOT NULL CONSTRAINT DF_Departamento_fechaCreacion DEFAULT (GETDATE()),
        fechaModificacion DATETIME          NULL,
        CONSTRAINT PK_Departamento PRIMARY KEY CLUSTERED (idDepartamento)
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_Departamento_nombre' AND object_id = OBJECT_ID(N'dir.Departamento'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_Departamento_nombre ON dir.Departamento (nombre) WHERE activo = 1;
GO

IF OBJECT_ID(N'dir.Sucursal', N'U') IS NULL
BEGIN
    CREATE TABLE dir.Sucursal
    (
        idSucursal        INT IDENTITY(1,1) NOT NULL,
        nombre            NVARCHAR(100)     NOT NULL,
        activo            BIT               NOT NULL CONSTRAINT DF_Sucursal_activo DEFAULT (1),
        fechaCreacion     DATETIME          NOT NULL CONSTRAINT DF_Sucursal_fechaCreacion DEFAULT (GETDATE()),
        fechaModificacion DATETIME          NULL,
        CONSTRAINT PK_Sucursal PRIMARY KEY CLUSTERED (idSucursal)
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_Sucursal_nombre' AND object_id = OBJECT_ID(N'dir.Sucursal'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_Sucursal_nombre ON dir.Sucursal (nombre) WHERE activo = 1;
GO

IF OBJECT_ID(N'dir.Ciudad', N'U') IS NULL
BEGIN
    CREATE TABLE dir.Ciudad
    (
        idCiudad          INT IDENTITY(1,1) NOT NULL,
        nombre            NVARCHAR(100)     NOT NULL,
        pais              NVARCHAR(2)       NOT NULL,
        activo            BIT               NOT NULL CONSTRAINT DF_Ciudad_activo DEFAULT (1),
        fechaCreacion     DATETIME          NOT NULL CONSTRAINT DF_Ciudad_fechaCreacion DEFAULT (GETDATE()),
        fechaModificacion DATETIME          NULL,
        CONSTRAINT PK_Ciudad PRIMARY KEY CLUSTERED (idCiudad),
        CONSTRAINT CK_Ciudad_pais CHECK (pais IN (N'MX', N'US'))
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_Ciudad_nombre_pais' AND object_id = OBJECT_ID(N'dir.Ciudad'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_Ciudad_nombre_pais ON dir.Ciudad (nombre, pais) WHERE activo = 1;
GO

/* ---------------------------------------------------------------------
   Cuentas y grupos
   --------------------------------------------------------------------- */
IF OBJECT_ID(N'dir.GrupoCuenta', N'U') IS NULL
BEGIN
    CREATE TABLE dir.GrupoCuenta
    (
        idGrupoCuenta     INT IDENTITY(1,1) NOT NULL,
        folio             NVARCHAR(30)      NULL,
        nombre            NVARCHAR(180)     NOT NULL,
        activo            BIT               NOT NULL CONSTRAINT DF_GrupoCuenta_activo DEFAULT (1),
        fechaCreacion     DATETIME          NOT NULL CONSTRAINT DF_GrupoCuenta_fechaCreacion DEFAULT (GETDATE()),
        fechaModificacion DATETIME          NULL,
        CONSTRAINT PK_GrupoCuenta PRIMARY KEY CLUSTERED (idGrupoCuenta)
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_GrupoCuenta_folio' AND object_id = OBJECT_ID(N'dir.GrupoCuenta'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_GrupoCuenta_folio ON dir.GrupoCuenta (folio) WHERE folio IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_GrupoCuenta_nombre' AND object_id = OBJECT_ID(N'dir.GrupoCuenta'))
    CREATE NONCLUSTERED INDEX IX_GrupoCuenta_nombre ON dir.GrupoCuenta (nombre);
GO

/* nombreComercial NO es unico a proposito: en EP360 hay cuentas con el mismo nombre
   (p. ej. ROYAL DYNAMIC SOLUTIONS, LLC) que hay que revisar a mano antes de fusionar. */
IF OBJECT_ID(N'dir.Cuenta', N'U') IS NULL
BEGIN
    CREATE TABLE dir.Cuenta
    (
        idCuenta          INT IDENTITY(1,1) NOT NULL,
        folio             NVARCHAR(30)      NULL,
        codigoCuenta      NVARCHAR(60)      NULL,
        nombreComercial   NVARCHAR(180)     NOT NULL,
        razonSocial       NVARCHAR(180)     NULL,
        rfc               NVARCHAR(20)      NULL,
        idGrupoCuenta     INT               NULL,
        idCiudad          INT               NULL,
        idSucursal        INT               NULL,
        activo            BIT               NOT NULL CONSTRAINT DF_Cuenta_activo DEFAULT (1),
        fechaCreacion     DATETIME          NOT NULL CONSTRAINT DF_Cuenta_fechaCreacion DEFAULT (GETDATE()),
        fechaModificacion DATETIME          NULL,
        CONSTRAINT PK_Cuenta PRIMARY KEY CLUSTERED (idCuenta),
        CONSTRAINT FK_Cuenta_GrupoCuenta FOREIGN KEY (idGrupoCuenta) REFERENCES dir.GrupoCuenta (idGrupoCuenta),
        CONSTRAINT FK_Cuenta_Ciudad      FOREIGN KEY (idCiudad)      REFERENCES dir.Ciudad (idCiudad),
        CONSTRAINT FK_Cuenta_Sucursal    FOREIGN KEY (idSucursal)    REFERENCES dir.Sucursal (idSucursal)
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_Cuenta_folio' AND object_id = OBJECT_ID(N'dir.Cuenta'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_Cuenta_folio ON dir.Cuenta (folio) WHERE folio IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Cuenta_nombreComercial' AND object_id = OBJECT_ID(N'dir.Cuenta'))
    CREATE NONCLUSTERED INDEX IX_Cuenta_nombreComercial ON dir.Cuenta (nombreComercial);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Cuenta_idGrupoCuenta' AND object_id = OBJECT_ID(N'dir.Cuenta'))
    CREATE NONCLUSTERED INDEX IX_Cuenta_idGrupoCuenta ON dir.Cuenta (idGrupoCuenta) WHERE idGrupoCuenta IS NOT NULL;
GO

/* ---------------------------------------------------------------------
   Personas
   tipoPersona: AD (usuario interno), Externo (usuario externo con credencial en seg),
                Contacto (persona sin acceso; puede escalar a Externo).
   --------------------------------------------------------------------- */
IF OBJECT_ID(N'dir.Persona', N'U') IS NULL
BEGIN
    CREATE TABLE dir.Persona
    (
        idPersona         INT IDENTITY(1,1) NOT NULL,
        tipoPersona       NVARCHAR(20)      NOT NULL,
        nombreCompleto    NVARCHAR(150)     NOT NULL,
        correo            NVARCHAR(200)     NULL,
        telefono          NVARCHAR(40)      NULL,
        extension         NVARCHAR(20)      NULL,
        puesto            NVARCHAR(120)     NULL,
        idDepartamento    INT               NULL,
        idSucursal        INT               NULL,
        activo            BIT               NOT NULL CONSTRAINT DF_Persona_activo DEFAULT (1),
        fechaCreacion     DATETIME          NOT NULL CONSTRAINT DF_Persona_fechaCreacion DEFAULT (GETDATE()),
        fechaModificacion DATETIME          NULL,
        idPersonaModifico INT               NULL,
        CONSTRAINT PK_Persona PRIMARY KEY CLUSTERED (idPersona),
        CONSTRAINT CK_Persona_tipoPersona CHECK (tipoPersona IN (N'AD', N'Externo', N'Contacto')),
        CONSTRAINT FK_Persona_Departamento FOREIGN KEY (idDepartamento)    REFERENCES dir.Departamento (idDepartamento),
        CONSTRAINT FK_Persona_Sucursal     FOREIGN KEY (idSucursal)        REFERENCES dir.Sucursal (idSucursal),
        CONSTRAINT FK_Persona_Modifico     FOREIGN KEY (idPersonaModifico) REFERENCES dir.Persona (idPersona)
    );
END
GO
/* El correo es unico entre personas activas. Hay contactos sin correo (solo telefono): NULL permitido. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_Persona_correo' AND object_id = OBJECT_ID(N'dir.Persona'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_Persona_correo ON dir.Persona (correo) WHERE correo IS NOT NULL AND activo = 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Persona_tipoPersona' AND object_id = OBJECT_ID(N'dir.Persona'))
    CREATE NONCLUSTERED INDEX IX_Persona_tipoPersona ON dir.Persona (tipoPersona, activo);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Persona_nombreCompleto' AND object_id = OBJECT_ID(N'dir.Persona'))
    CREATE NONCLUSTERED INDEX IX_Persona_nombreCompleto ON dir.Persona (nombreCompleto);
GO

/* Identidad de AD (1:1 con Persona). objectSid es la identidad estable; samAccountName puede cambiar. */
IF OBJECT_ID(N'dir.UsuarioAD', N'U') IS NULL
BEGIN
    CREATE TABLE dir.UsuarioAD
    (
        idPersona                 INT           NOT NULL,
        objectSid                 VARBINARY(85) NOT NULL,
        samAccountName            NVARCHAR(100) NOT NULL,
        userPrincipalName         NVARCHAR(200) NULL,
        correoAD                  NVARCHAR(200) NULL,
        habilitadoAD              BIT           NOT NULL CONSTRAINT DF_UsuarioAD_habilitadoAD DEFAULT (1),
        fechaUltimaSincronizacion DATETIME      NULL,
        CONSTRAINT PK_UsuarioAD PRIMARY KEY CLUSTERED (idPersona),
        CONSTRAINT FK_UsuarioAD_Persona FOREIGN KEY (idPersona) REFERENCES dir.Persona (idPersona)
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_UsuarioAD_objectSid' AND object_id = OBJECT_ID(N'dir.UsuarioAD'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_UsuarioAD_objectSid ON dir.UsuarioAD (objectSid);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_UsuarioAD_samAccountName' AND object_id = OBJECT_ID(N'dir.UsuarioAD'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_UsuarioAD_samAccountName ON dir.UsuarioAD (samAccountName);
GO

/* Contacto de cuenta (muchos a muchos): una persona puede ser contacto de varias cuentas.
   Para un usuario externo, esta tabla define a que cuentas pertenece. */
IF OBJECT_ID(N'dir.PersonaCuenta', N'U') IS NULL
BEGIN
    CREATE TABLE dir.PersonaCuenta
    (
        idPersonaCuenta   INT IDENTITY(1,1) NOT NULL,
        idPersona         INT               NOT NULL,
        idCuenta          INT               NOT NULL,
        categoria         NVARCHAR(30)      NULL,
        puestoEnCuenta    NVARCHAR(120)     NULL,
        activo            BIT               NOT NULL CONSTRAINT DF_PersonaCuenta_activo DEFAULT (1),
        fechaCreacion     DATETIME          NOT NULL CONSTRAINT DF_PersonaCuenta_fechaCreacion DEFAULT (GETDATE()),
        fechaModificacion DATETIME          NULL,
        CONSTRAINT PK_PersonaCuenta PRIMARY KEY CLUSTERED (idPersonaCuenta),
        CONSTRAINT FK_PersonaCuenta_Persona FOREIGN KEY (idPersona) REFERENCES dir.Persona (idPersona),
        CONSTRAINT FK_PersonaCuenta_Cuenta  FOREIGN KEY (idCuenta)  REFERENCES dir.Cuenta (idCuenta)
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_PersonaCuenta_activa' AND object_id = OBJECT_ID(N'dir.PersonaCuenta'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_PersonaCuenta_activa ON dir.PersonaCuenta (idPersona, idCuenta) WHERE activo = 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PersonaCuenta_idCuenta' AND object_id = OBJECT_ID(N'dir.PersonaCuenta'))
    CREATE NONCLUSTERED INDEX IX_PersonaCuenta_idCuenta ON dir.PersonaCuenta (idCuenta, activo);
GO

/* Medios de contacto adicionales (Balance guarda cada telefono o correo como una fila). */
IF OBJECT_ID(N'dir.PersonaMedioContacto', N'U') IS NULL
BEGIN
    CREATE TABLE dir.PersonaMedioContacto
    (
        idMedio           INT IDENTITY(1,1) NOT NULL,
        idPersona         INT               NOT NULL,
        tipo              NVARCHAR(20)      NOT NULL,
        categoria         NVARCHAR(30)      NULL,
        valor             NVARCHAR(180)     NOT NULL,
        extension         NVARCHAR(20)      NULL,
        activo            BIT               NOT NULL CONSTRAINT DF_PersonaMedioContacto_activo DEFAULT (1),
        fechaCreacion     DATETIME          NOT NULL CONSTRAINT DF_PersonaMedioContacto_fechaCreacion DEFAULT (GETDATE()),
        fechaModificacion DATETIME          NULL,
        CONSTRAINT PK_PersonaMedioContacto PRIMARY KEY CLUSTERED (idMedio),
        CONSTRAINT CK_PersonaMedioContacto_tipo CHECK (tipo IN (N'Telefono', N'Correo')),
        CONSTRAINT FK_PersonaMedioContacto_Persona FOREIGN KEY (idPersona) REFERENCES dir.Persona (idPersona)
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PersonaMedioContacto_idPersona' AND object_id = OBJECT_ID(N'dir.PersonaMedioContacto'))
    CREATE NONCLUSTERED INDEX IX_PersonaMedioContacto_idPersona ON dir.PersonaMedioContacto (idPersona, activo);
GO

PRINT '02_TablasNucleoDirectorio.sql terminado.';
SELECT SCHEMA_NAME(schema_id) AS esquema, name AS tabla FROM sys.tables WHERE SCHEMA_NAME(schema_id) IN (N'dir', N'seg', N'sync') ORDER BY 1, 2;
GO
