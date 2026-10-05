/* =====================================================================
   04_CredencialesExternas.sql
   BD global: EP360_Logistics   (servidor 60, maestro)
   REQUIERE haber corrido antes: 01, 02 y 03.

   Esquema seg: credenciales de usuarios EXTERNOS (correo + contrasena propios).
   Un contacto (dir.Persona tipo Contacto) escala a usuario externo al recibir una
   credencial: la persona conserva su idPersona, solo cambia a tipoPersona = 'Externo'.

   Crea:
     Tabla : seg.CredencialExterna (1:1 con dir.Persona)
     SPs   : seg.sp_InvitarUsuarioExterno, seg.sp_ActivarCredencial,
             seg.sp_SolicitarRestablecerPassword, seg.sp_ObtenerCredencialPorCorreo,
             seg.sp_RegistrarIntentoFallido, seg.sp_RegistrarAccesoExitoso,
             seg.sp_DesactivarCredencial
   Reemplaza: dir.sp_DesactivarPersona (ahora tambien desactiva la credencial).

   IMPORTANTE - lo que hace la BD y lo que hace la aplicacion:
     * La aplicacion (C#) genera el token de invitacion y calcula los hashes (PBKDF2 para la
       contrasena, SHA-256 para el token). La BD solo guarda HASHES; nunca el token ni la
       contrasena en claro, y nunca compara contrasenas (eso lo hace C# con el hash que
       devuelve seg.sp_ObtenerCredencialPorCorreo, como hoy en EP360).
     * Estos SPs SOLO se ejecutan en el maestro 60 (los portales publicos viven ahi).
       La tabla se sincroniza tambien al 11 (decision del proyecto: el 11 es siempre local).

   Es IDEMPOTENTE. No inserta datos ni toca otras bases.
   Quien lo corre: el dueno del proyecto, en SSMS, conectado al 60.
   ===================================================================== */

USE EP360_Logistics;
GO

/* ---------------------------------------------------------------------
   seg.CredencialExterna
   estado: Invitado (aun sin contrasena), Activo, Bloqueado (bloqueo manual de un admin),
           Desactivado (baja). El bloqueo POR INTENTOS FALLIDOS es temporal y usa
           bloqueadoHasta sin cambiar el estado.
   tokenHash: hash del token de invitacion O de restablecimiento (nunca el token).
   --------------------------------------------------------------------- */
IF OBJECT_ID(N'seg.CredencialExterna', N'U') IS NULL
BEGIN
    CREATE TABLE seg.CredencialExterna
    (
        idPersona           INT           NOT NULL,
        passwordHash        NVARCHAR(256) NULL,
        estado              NVARCHAR(20)  NOT NULL,
        tokenHash           NVARCHAR(128) NULL,
        fechaExpiraToken    DATETIME      NULL,
        debeCambiarPassword BIT           NOT NULL CONSTRAINT DF_CredencialExterna_debeCambiar DEFAULT (0),
        fechaCambioPassword DATETIME      NULL,
        intentosFallidos    INT           NOT NULL CONSTRAINT DF_CredencialExterna_intentos DEFAULT (0),
        bloqueadoHasta      DATETIME      NULL,
        fechaCreacion       DATETIME      NOT NULL CONSTRAINT DF_CredencialExterna_fechaCreacion DEFAULT (GETDATE()),
        fechaModificacion   DATETIME      NULL,
        CONSTRAINT PK_CredencialExterna PRIMARY KEY CLUSTERED (idPersona),
        CONSTRAINT FK_CredencialExterna_Persona FOREIGN KEY (idPersona) REFERENCES dir.Persona (idPersona),
        CONSTRAINT CK_CredencialExterna_estado CHECK (estado IN (N'Invitado', N'Activo', N'Bloqueado', N'Desactivado'))
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_CredencialExterna_tokenHash' AND object_id = OBJECT_ID(N'seg.CredencialExterna'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_CredencialExterna_tokenHash ON seg.CredencialExterna (tokenHash) WHERE tokenHash IS NOT NULL;
GO

/* =====================================================================
   INVITAR: convierte un contacto en usuario externo (o reinvita).
   El token llega YA hasheado; la app conserva el token en claro solo para armar el enlace del correo.
   ===================================================================== */
CREATE OR ALTER PROCEDURE seg.sp_InvitarUsuarioExterno
    @idPersona         INT,
    @tokenHash         NVARCHAR(128),
    @fechaExpiraToken  DATETIME,
    @idPersonaModifico INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @tokenHash IS NULL OR @tokenHash = N'' THROW 50021, N'El token es obligatorio.', 1;
    IF @fechaExpiraToken IS NULL OR @fechaExpiraToken <= GETDATE() THROW 50022, N'La fecha de expiracion del token debe ser futura.', 1;

    DECLARE @tipo NVARCHAR(20), @activa BIT, @correo NVARCHAR(200);
    SELECT @tipo = tipoPersona, @activa = activo, @correo = correo FROM dir.Persona WHERE idPersona = @idPersona;

    IF @tipo IS NULL THROW 50003, N'La persona no existe.', 1;
    IF @activa = 0   THROW 50008, N'La persona esta inactiva.', 1;
    IF @tipo = N'AD' THROW 50023, N'Un usuario de AD no puede tener credencial externa.', 1;
    IF @correo IS NULL THROW 50006, N'Un usuario externo necesita correo.', 1;

    BEGIN TRAN;

    DECLARE @estadoActual NVARCHAR(20) =
        (SELECT estado FROM seg.CredencialExterna WITH (UPDLOCK, HOLDLOCK) WHERE idPersona = @idPersona);

    IF @estadoActual IN (N'Activo', N'Bloqueado')
    BEGIN
        ROLLBACK;
        THROW 50024, N'La persona ya tiene una credencial vigente. Usa el restablecimiento de contrasena.', 1;
    END

    IF @estadoActual IS NULL
        INSERT INTO seg.CredencialExterna (idPersona, estado, tokenHash, fechaExpiraToken)
        VALUES (@idPersona, N'Invitado', @tokenHash, @fechaExpiraToken);
    ELSE
        -- Invitado (nueva invitacion) o Desactivado (reinvitacion): vuelve a empezar sin contrasena.
        UPDATE seg.CredencialExterna
        SET estado = N'Invitado', passwordHash = NULL, tokenHash = @tokenHash, fechaExpiraToken = @fechaExpiraToken,
            intentosFallidos = 0, bloqueadoHasta = NULL, fechaModificacion = GETDATE()
        WHERE idPersona = @idPersona;

    IF @tipo = N'Contacto'
        UPDATE dir.Persona SET tipoPersona = N'Externo', fechaModificacion = GETDATE(), idPersonaModifico = @idPersonaModifico
        WHERE idPersona = @idPersona;

    COMMIT;
    SELECT @idPersona AS idPersona, @correo AS correo;
END
GO

/* =====================================================================
   ACTIVAR / FIJAR CONTRASENA con un token (invitacion o restablecimiento).
   Mensaje deliberadamente generico: no revela si el token existio, expiro o ya se uso.
   ===================================================================== */
CREATE OR ALTER PROCEDURE seg.sp_ActivarCredencial
    @tokenHash    NVARCHAR(128),
    @passwordHash NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @tokenHash IS NULL OR @tokenHash = N'' OR @passwordHash IS NULL OR @passwordHash = N''
        THROW 50025, N'El enlace no es valido o ya expiro.', 1;

    BEGIN TRAN;

    DECLARE @idPersona INT;
    SELECT @idPersona = c.idPersona
    FROM seg.CredencialExterna c WITH (UPDLOCK, HOLDLOCK)
    JOIN dir.Persona p ON p.idPersona = c.idPersona AND p.activo = 1
    WHERE c.tokenHash = @tokenHash AND c.fechaExpiraToken > GETDATE() AND c.estado IN (N'Invitado', N'Activo');

    IF @idPersona IS NULL
    BEGIN
        ROLLBACK;
        THROW 50025, N'El enlace no es valido o ya expiro.', 1;
    END

    UPDATE seg.CredencialExterna
    SET passwordHash = @passwordHash, estado = N'Activo', tokenHash = NULL, fechaExpiraToken = NULL,
        debeCambiarPassword = 0, fechaCambioPassword = GETDATE(), intentosFallidos = 0, bloqueadoHasta = NULL,
        fechaModificacion = GETDATE()
    WHERE idPersona = @idPersona;

    COMMIT;
    SELECT @idPersona AS idPersona;
END
GO

/* =====================================================================
   RESTABLECER: genera un token de restablecimiento para una credencial ACTIVA.
   Si el correo no corresponde a ninguna credencial activa, NO devuelve filas y NO falla:
   la app debe responder siempre lo mismo (no se revela si el correo existe).
   ===================================================================== */
CREATE OR ALTER PROCEDURE seg.sp_SolicitarRestablecerPassword
    @correo           NVARCHAR(200),
    @tokenHash        NVARCHAR(128),
    @fechaExpiraToken DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @tokenHash IS NULL OR @tokenHash = N'' THROW 50021, N'El token es obligatorio.', 1;
    IF @fechaExpiraToken IS NULL OR @fechaExpiraToken <= GETDATE() THROW 50022, N'La fecha de expiracion del token debe ser futura.', 1;

    DECLARE @idPersona INT;
    SELECT @idPersona = c.idPersona
    FROM seg.CredencialExterna c
    JOIN dir.Persona p ON p.idPersona = c.idPersona AND p.activo = 1 AND p.tipoPersona = N'Externo'
    WHERE p.correo = @correo AND c.estado = N'Activo';

    IF @idPersona IS NULL RETURN;

    UPDATE seg.CredencialExterna
    SET tokenHash = @tokenHash, fechaExpiraToken = @fechaExpiraToken, fechaModificacion = GETDATE()
    WHERE idPersona = @idPersona;

    SELECT p.idPersona, p.nombreCompleto, p.correo FROM dir.Persona p WHERE p.idPersona = @idPersona;
END
GO

/* =====================================================================
   LOGIN: devuelve lo necesario para que C# compare la contrasena (PBKDF2).
   Solo personas Externas ACTIVAS con credencial. No devuelve filas si no hay.
   ===================================================================== */
CREATE OR ALTER PROCEDURE seg.sp_ObtenerCredencialPorCorreo @correo NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT p.idPersona, p.nombreCompleto, p.correo,
           c.passwordHash, c.estado, c.intentosFallidos, c.bloqueadoHasta, c.debeCambiarPassword,
           CAST(CASE WHEN c.bloqueadoHasta IS NOT NULL AND c.bloqueadoHasta > GETDATE() THEN 1 ELSE 0 END AS BIT) AS bloqueadoTemporalmente
    FROM dir.Persona p
    JOIN seg.CredencialExterna c ON c.idPersona = p.idPersona
    WHERE p.correo = @correo AND p.activo = 1 AND p.tipoPersona = N'Externo';
END
GO

/* Suma un intento fallido; al llegar al maximo bloquea temporalmente (estado no cambia). */
CREATE OR ALTER PROCEDURE seg.sp_RegistrarIntentoFallido
    @idPersona      INT,
    @maxIntentos    INT = 5,
    @minutosBloqueo INT = 15
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE seg.CredencialExterna
    SET intentosFallidos = intentosFallidos + 1,
        bloqueadoHasta = CASE WHEN intentosFallidos + 1 >= @maxIntentos THEN DATEADD(MINUTE, @minutosBloqueo, GETDATE()) ELSE bloqueadoHasta END,
        fechaModificacion = GETDATE()
    WHERE idPersona = @idPersona AND estado = N'Activo';

    SELECT intentosFallidos, bloqueadoHasta FROM seg.CredencialExterna WHERE idPersona = @idPersona;
END
GO

CREATE OR ALTER PROCEDURE seg.sp_RegistrarAccesoExitoso @idPersona INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE seg.CredencialExterna
    SET intentosFallidos = 0, bloqueadoHasta = NULL, fechaModificacion = GETDATE()
    WHERE idPersona = @idPersona AND (intentosFallidos <> 0 OR bloqueadoHasta IS NOT NULL);
END
GO

CREATE OR ALTER PROCEDURE seg.sp_DesactivarCredencial @idPersona INT, @idPersonaModifico INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM seg.CredencialExterna WHERE idPersona = @idPersona) THROW 50026, N'La persona no tiene credencial.', 1;

    UPDATE seg.CredencialExterna
    SET estado = N'Desactivado', tokenHash = NULL, fechaExpiraToken = NULL, fechaModificacion = GETDATE()
    WHERE idPersona = @idPersona;

    UPDATE dir.Persona SET fechaModificacion = GETDATE(), idPersonaModifico = @idPersonaModifico WHERE idPersona = @idPersona;
END
GO

/* =====================================================================
   dir.sp_DesactivarPersona (reemplaza la del script 03): tambien desactiva la credencial.
   ===================================================================== */
CREATE OR ALTER PROCEDURE dir.sp_DesactivarPersona @idPersona INT, @idPersonaModifico INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF NOT EXISTS (SELECT 1 FROM dir.Persona WHERE idPersona = @idPersona) THROW 50003, N'La persona no existe.', 1;

    BEGIN TRAN;
    UPDATE dir.PersonaCuenta SET activo = 0, fechaModificacion = GETDATE() WHERE idPersona = @idPersona AND activo = 1;
    UPDATE dir.PersonaMedioContacto SET activo = 0, fechaModificacion = GETDATE() WHERE idPersona = @idPersona AND activo = 1;
    UPDATE seg.CredencialExterna
    SET estado = N'Desactivado', tokenHash = NULL, fechaExpiraToken = NULL, fechaModificacion = GETDATE()
    WHERE idPersona = @idPersona AND estado <> N'Desactivado';
    UPDATE dir.Persona SET activo = 0, fechaModificacion = GETDATE(), idPersonaModifico = @idPersonaModifico WHERE idPersona = @idPersona;
    COMMIT;
END
GO

PRINT '04_CredencialesExternas.sql terminado.';
SELECT SCHEMA_NAME(schema_id) AS esquema, name, type_desc FROM sys.objects
WHERE SCHEMA_NAME(schema_id) = N'seg' ORDER BY type_desc, name;
GO
