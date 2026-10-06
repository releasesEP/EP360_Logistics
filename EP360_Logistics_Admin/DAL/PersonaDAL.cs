using System.Collections.Generic;
using System.Data;
using EP360_Logistics_Admin.DAL.Infraestructura;
using EP360_Logistics_Admin.Models;
using static EP360_Logistics_Admin.DAL.Infraestructura.AccesoSP;

namespace EP360_Logistics_Admin.DAL
{
    public class PersonaDAL
    {
        private static PersonaModel MapaLista(IDataRecord r)
        {
            return new PersonaModel
            {
                IdPersona = r.Entero("idPersona"),
                TipoPersona = r.Texto("tipoPersona"),
                NombreCompleto = r.Texto("nombreCompleto"),
                Correo = r.Texto("correo"),
                Telefono = r.Texto("telefono"),
                Extension = r.Texto("extension"),
                Puesto = r.Texto("puesto"),
                IdDepartamento = r.EnteroNulo("idDepartamento"),
                Departamento = r.Texto("departamento"),
                IdSucursal = r.EnteroNulo("idSucursal"),
                Sucursal = r.Texto("sucursal"),
                SamAccountName = r.Texto("samAccountName"),
                HabilitadoAD = r.BooleanoNulo("habilitadoAD"),
                Activo = r.Booleano("activo")
            };
        }

        private static PersonaModel MapaDetalle(IDataRecord r)
        {
            return new PersonaModel
            {
                IdPersona = r.Entero("idPersona"),
                TipoPersona = r.Texto("tipoPersona"),
                NombreCompleto = r.Texto("nombreCompleto"),
                Correo = r.Texto("correo"),
                Telefono = r.Texto("telefono"),
                Extension = r.Texto("extension"),
                Puesto = r.Texto("puesto"),
                IdDepartamento = r.EnteroNulo("idDepartamento"),
                Departamento = r.Texto("departamento"),
                IdSucursal = r.EnteroNulo("idSucursal"),
                Sucursal = r.Texto("sucursal"),
                SamAccountName = r.Texto("samAccountName"),
                UserPrincipalName = r.Texto("userPrincipalName"),
                HabilitadoAD = r.BooleanoNulo("habilitadoAD"),
                FechaUltimaSincronizacion = r.Fecha("fechaUltimaSincronizacion"),
                Activo = r.Booleano("activo"),
                FechaCreacion = r.Fecha("fechaCreacion"),
                FechaModificacion = r.Fecha("fechaModificacion")
            };
        }

        public List<PersonaModel> Listar(string tipoPersona, bool incluirInactivos, string buscar)
        {
            return Lista("dir.sp_ObtenerPersonas", MapaLista,
                P("@tipoPersona", string.IsNullOrWhiteSpace(tipoPersona) ? null : tipoPersona),
                P("@incluirInactivos", incluirInactivos),
                P("@buscar", string.IsNullOrWhiteSpace(buscar) ? null : buscar.Trim()));
        }

        // Todas (activas e inactivas, sin el tope de 500 del SP): el listado filtra y pagina en memoria.
        public List<PersonaModel> ListarTodas()
        {
            return Lista("dir.sp_ObtenerPersonas", MapaLista, P("@incluirInactivos", true), P("@maximo", 100000));
        }

        public PersonaModel ObtenerPorId(int id)
        {
            return Uno("dir.sp_ObtenerPersonaPorId", MapaDetalle, P("@idPersona", id));
        }

        // El idPersonaModifico queda en NULL hasta que los administradores existan como Persona de AD
        // (los sincroniza sync.sp_SincronizarUsuarioAD); entonces se pasara el id del administrador.
        public int Insertar(PersonaModel m)
        {
            return Escalar("dir.sp_InsertarPersona",
                P("@tipoPersona", m.TipoPersona), P("@nombreCompleto", m.NombreCompleto), P("@correo", m.Correo),
                P("@telefono", m.Telefono), P("@extension", m.Extension), P("@puesto", m.Puesto),
                P("@idDepartamento", m.IdDepartamento), P("@idSucursal", m.IdSucursal), P("@idPersonaModifico", null));
        }

        public void Actualizar(PersonaModel m)
        {
            Ejecutar("dir.sp_ActualizarPersona",
                P("@idPersona", m.IdPersona), P("@nombreCompleto", m.NombreCompleto), P("@correo", m.Correo),
                P("@telefono", m.Telefono), P("@extension", m.Extension), P("@puesto", m.Puesto),
                P("@idDepartamento", m.IdDepartamento), P("@idSucursal", m.IdSucursal), P("@idPersonaModifico", null));
        }

        public void Desactivar(int id) { Ejecutar("dir.sp_DesactivarPersona", P("@idPersona", id), P("@idPersonaModifico", null)); }
        public void Reactivar(int id) { Ejecutar("dir.sp_ReactivarPersona", P("@idPersona", id), P("@idPersonaModifico", null)); }

        public List<PersonaCuentaModel> CuentasDePersona(int idPersona)
        {
            return Lista("dir.sp_ObtenerCuentasDePersona", r => new PersonaCuentaModel
            {
                IdPersonaCuenta = r.Entero("idPersonaCuenta"),
                IdPersona = idPersona,
                IdCuenta = r.Entero("idCuenta"),
                Cuenta = r.Texto("cuenta"),
                GrupoCuenta = r.Texto("grupoCuenta"),
                Categoria = r.Texto("categoria"),
                PuestoEnCuenta = r.Texto("puestoEnCuenta")
            }, P("@idPersona", idPersona));
        }

        public List<PersonaCuentaModel> PersonasDeCuenta(int idCuenta)
        {
            return Lista("dir.sp_ObtenerPersonasDeCuenta", r => new PersonaCuentaModel
            {
                IdPersonaCuenta = r.Entero("idPersonaCuenta"),
                IdPersona = r.Entero("idPersona"),
                IdCuenta = idCuenta,
                TipoPersona = r.Texto("tipoPersona"),
                NombrePersona = r.Texto("nombreCompleto"),
                Correo = r.Texto("correo"),
                Telefono = r.Texto("telefono"),
                Categoria = r.Texto("categoria"),
                PuestoEnCuenta = r.Texto("puestoEnCuenta")
            }, P("@idCuenta", idCuenta));
        }

        public void Vincular(int idPersona, int idCuenta, string categoria, string puestoEnCuenta)
        {
            Escalar("dir.sp_VincularPersonaCuenta",
                P("@idPersona", idPersona), P("@idCuenta", idCuenta),
                P("@categoria", string.IsNullOrWhiteSpace(categoria) ? null : categoria.Trim()),
                P("@puestoEnCuenta", string.IsNullOrWhiteSpace(puestoEnCuenta) ? null : puestoEnCuenta.Trim()));
        }

        public void Desvincular(int idPersona, int idCuenta)
        {
            Ejecutar("dir.sp_DesvincularPersonaCuenta", P("@idPersona", idPersona), P("@idCuenta", idCuenta));
        }
    }
}
