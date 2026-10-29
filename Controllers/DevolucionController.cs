using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SigelabBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DevolucionController : ControllerBase
    {
        private readonly string _connectionString;

        public DevolucionController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("SqlServerConn")
                ?? configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Falta SqlServerConn o DefaultConnection en ConnectionStrings.");
        }

        // Conserva las columnas y todos los conjuntos de resultados del SP.
        private static async Task<List<List<Dictionary<string, object?>>>> LeerResultadosAsync(SqlDataReader reader)
        {
            var conjuntos = new List<List<Dictionary<string, object?>>>();
            do
            {
                if (reader.FieldCount == 0) continue;
                var filas = new List<Dictionary<string, object?>>();
                while (await reader.ReadAsync())
                {
                    var fila = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                        fila[reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
                    filas.Add(fila);
                }
                conjuntos.Add(filas);
            } while (await reader.NextResultAsync());
            return conjuntos;
        }

        private async Task<IActionResult> EjecutarSpAsync(string nombreSp, object? cuerpo = null)
        {
            try
            {
                await using var conexion = new SqlConnection(_connectionString);
                await conexion.OpenAsync();
                await using var comando = new SqlCommand(nombreSp, conexion)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 60
                };
                // Los cinco SP de devoluciones reciben @json.
                comando.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value =
                    JsonSerializer.Serialize(cuerpo ?? new { });
                await using var lector = await comando.ExecuteReaderAsync();
                var resultados = await LeerResultadosAsync(lector);
                // No inventamos un resultado exitoso: retornamos lo que realmente envió SQL.
                return Ok(resultados.Count == 1 ? (object)resultados[0] : resultados);
            }
            catch (SqlException ex)
            {
                // Registrar ex en un ILogger en producción; no divulgar detalles internos.
                return StatusCode(500, new { exito = false, mensaje = "Error al ejecutar el procedimiento almacenado.", codigoSql = ex.Number });
            }
            catch (Exception)
            {
                return StatusCode(500, new { exito = false, mensaje = "Error interno del servidor." });
            }
        }

        // GET api/Devolucion/pendientes?idSolicitante=8
        [HttpGet("pendientes")]
        public Task<IActionResult> ListarPendientes([FromQuery] int? idSolicitante = null)
        {
            var datos = new Dictionary<string, object?>();
            if (idSolicitante.HasValue) datos["idSolicitante"] = idSolicitante.Value;
            return EjecutarSpAsync("dbo.sp_devolucion_listar_pendientes", new { datos });
        }

        // GET api/Devolucion/prestamo/25/detalle
        [HttpGet("prestamo/{idPrestamo:int}/detalle")]
        public Task<IActionResult> ObtenerDetallePorPrestamo(int idPrestamo) =>
            EjecutarSpAsync("dbo.sp_devolucion_obtener_detalle", new { datos = new { idPrestamo } });

        // GET api/Devolucion/1/detalle
        [HttpGet("{idDevolucion:int}/detalle")]
        public Task<IActionResult> ObtenerDetallePorDevolucion(int idDevolucion) =>
            EjecutarSpAsync("dbo.sp_devolucion_obtener_detalle", new { datos = new { idDevolucion } });

        // GET api/Devolucion/prestamo/25/equipos-pendientes
        [HttpGet("prestamo/{idPrestamo:int}/equipos-pendientes")]
        public Task<IActionResult> ListarEquiposPendientes(int idPrestamo) =>
            EjecutarSpAsync("dbo.sp_devolucion_listar_equipos_pendientes", new { datos = new { idPrestamo } });

        // GET api/Devolucion/prestamo/25/qr/QR-PRUEBA-EQ-001
        [HttpGet("prestamo/{idPrestamo:int}/qr/{codigoQR}")]
        public Task<IActionResult> BuscarPendientePorQr(int idPrestamo, string codigoQR) =>
            EjecutarSpAsync("dbo.sp_devolucion_buscar_pendiente_por_qr", new { datos = new { idPrestamo, codigoQR } });

        // POST api/Devolucion/recepcion
        // Body: {"usuarioAccionante":{"idUsuario":6},"datos":{"idPrestamo":25,
        // "observaciones":"Recepcion parcial","equipos":[{"idEquipo":1,"condicion":"BUENO","observaciones":"Sin novedades"}]}}
        [HttpPost("recepcion")]
        public Task<IActionResult> RegistrarRecepcion([FromBody] JsonElement cuerpo)
        {
            if (cuerpo.ValueKind != JsonValueKind.Object ||
                !cuerpo.TryGetProperty("usuarioAccionante", out _) ||
                !cuerpo.TryGetProperty("datos", out _))
                return Task.FromResult<IActionResult>(BadRequest(new { exito = false, mensaje = "Se requieren usuarioAccionante y datos." }));
            // Se preserva el JSON original, incluyendo la lista de equipos.
            return EjecutarSpAsync("dbo.sp_devolucion_registrar_recepcion", cuerpo);
        }
    }
}
