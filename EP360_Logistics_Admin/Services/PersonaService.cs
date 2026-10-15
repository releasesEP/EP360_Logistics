using System;
using System.Collections.Generic;
using System.Linq;
using EP360_Logistics_Admin.DAL;
using EP360_Logistics_Admin.Models;

namespace EP360_Logistics_Admin.Services
{
    public class PersonaService
    {
        private const int LongitudMinimaPassword = 10;   // igual que EP360

        private readonly PersonaDAL _dal = new PersonaDAL();
        private readonly CredencialDAL _credencialDal = new CredencialDAL();

        public List<PersonaModel> Listar(string tipoPersona, bool incluirInactivos, string buscar)
        {
            return _dal.Listar(tipoPersona, incluirInactivos, buscar);
        }

        public List<PersonaModel> ListarTodas() { return _dal.ListarTodas(); }

        // Personas que se pueden vincular como contacto de una cuenta (todo menos usuarios de AD).
        public List<PersonaModel> ListarVinculables()
        {
            return _dal.Listar("Contacto", false, null).Concat(_dal.Listar("Externo", false, null)).OrderBy(p => p.NombreCompleto).ToList();
        }

        public PersonaModel ObtenerPorId(int id) { return _dal.ObtenerPorId(id); }

        // Acceso como usuario EXTERNO (correo + contrasena propios). La contrasena se convierte en hash aqui
        // (mismo PBKDF2 que EP360) y la BD solo recibe el hash. Un contacto pasa a usuario externo al recibir su
        // primera contrasena. Los usuarios de AD entran con Windows y no tienen credencial externa.
        public CredencialEstadoModel ObtenerCredencial(int idPersona) { return _credencialDal.Obtener(idPersona); }

        public void FijarPassword(int idPersona, string password, string confirmar)
        {
            if (string.IsNullOrEmpty(password) || password.Length < LongitudMinimaPassword)
                throw new InvalidOperationException("La contraseña debe tener al menos " + LongitudMinimaPassword + " caracteres.");
            if (password != confirmar)
                throw new InvalidOperationException("La confirmación de contraseña no coincide.");

            _credencialDal.FijarPassword(idPersona, Helpers.PasswordHasher.GenerarHash(password));
        }
        public List<PersonaCuentaModel> CuentasDePersona(int idPersona) { return _dal.CuentasDePersona(idPersona); }

        public int Crear(PersonaModel m) { Normalizar(m); return _dal.Insertar(m); }
        public void Actualizar(PersonaModel m) { Normalizar(m); _dal.Actualizar(m); }
        public void Desactivar(int id) { _dal.Desactivar(id); }
        public void Reactivar(int id) { _dal.Reactivar(id); }

        public void Vincular(int idPersona, int idCuenta, string categoria, string puesto) { _dal.Vincular(idPersona, idCuenta, categoria, puesto); }
        public void Desvincular(int idPersona, int idCuenta) { _dal.Desvincular(idPersona, idCuenta); }

        // Liga a la persona a varias cuentas de una vez: las elegidas una por una mas todas las cuentas activas del
        // grupo elegido (foto del momento: las cuentas que se agreguen despues al grupo no se ligan solas).
        // Devuelve las cuentas a las que quedo ligada.
        public List<int> VincularVarias(int idPersona, IEnumerable<int> idCuentas, int? idGrupoCuenta, string categoria, string puesto)
        {
            var destino = new List<int>(idCuentas ?? new int[0]);
            if (idGrupoCuenta.HasValue)
                destino.AddRange(new CuentaDAL().Listar(false, idGrupoCuenta, null).Select(c => c.IdCuenta));
            // Las cuentas donde ya es contacto se saltan: sp_VincularPersonaCuenta sobrescribiria su categoria y puesto.
            var yaLigadas = _dal.CuentasDePersona(idPersona).Select(c => c.IdCuenta).ToList();
            destino = destino.Distinct().Where(id => !yaLigadas.Contains(id)).ToList();

            // Si una falla, las anteriores ya quedaron ligadas; volver a intentar solo completa las que faltan.
            foreach (var idCuenta in destino) _dal.Vincular(idPersona, idCuenta, categoria, puesto);
            return destino;
        }

        private static void Normalizar(PersonaModel m)
        {
            m.NombreCompleto = m.NombreCompleto == null ? null : m.NombreCompleto.Trim();
            m.Correo = Vacio(m.Correo);
            m.Telefono = Vacio(m.Telefono);
            m.Extension = Vacio(m.Extension);
            m.Puesto = Vacio(m.Puesto);
        }

        private static string Vacio(string s) { return string.IsNullOrWhiteSpace(s) ? null : s.Trim(); }
    }
}
