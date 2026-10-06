using System;
using EP360_Logistics_Admin.DAL;
using EP360_Logistics_Admin.DAL.Infraestructura;

namespace EP360_Logistics_Admin.Helpers
{
    // Aviso corto para el menu: solo habla si hay algo que atender. Se consulta a lo mucho una vez por minuto
    // (guarda el resultado en memoria) para no pegarle a la base en cada pagina.
    public static class AlertaReplicacion
    {
        private static readonly object Candado = new object();
        private static readonly TimeSpan Vigencia = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan PendienteSospechosa = TimeSpan.FromMinutes(5);
        private static DateTime _consultadoEn = DateTime.MinValue;
        private static string _texto;

        // null = todo bien (o la copia no esta configurada).
        public static string Texto()
        {
            if (!Replicador.Configurada) return null;
            lock (Candado)
            {
                if (DateTime.UtcNow - _consultadoEn < Vigencia) return _texto;
                _consultadoEn = DateTime.UtcNow;
                try
                {
                    var r = new ReplicacionDAL().Resumen();
                    if (r == null) _texto = null;
                    else if (r.Divergentes > 0) _texto = r.Divergentes + " operación(es) divergente(s) en la copia del 11";
                    else if (r.Pendientes > 0 && r.PendienteMasAntigua.HasValue && DateTime.Now - r.PendienteMasAntigua.Value > PendienteSospechosa)
                        _texto = r.Pendientes + " operación(es) sin copiar al 11 desde hace más de 5 minutos";
                    else _texto = null;
                }
                catch (Exception)
                {
                    // Sin poder leer la cola no se puede afirmar nada; la pantalla de Replicacion explica el motivo.
                    _texto = "No se pudo revisar el estado de la copia en el 11";
                }
                return _texto;
            }
        }
    }
}
