using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SigelabBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PrestamoController : ControllerBase
    {
        private readonly string _connectionString;

        public PrestamoController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("SqlServerConn")
                ?? configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("No existe una cadena de conexión SQL Server configurada.");
        }

        // Ejecuta un SP y conserva todos sus conjuntos de resultados, incluidos los detalles.
        private async Task<IActionResult> EjecutarSp(string nombre, object? payload = null)
        {
            try
            {
                await using var conexion = new SqlConnection(_connectionString);
                await conexion.OpenAsync();
                await using var comando = new SqlCommand(nombre, conexion)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 60
                };
                if (payload != null)
                {
                    var json = payload is string texto ? texto : JsonSerializer.Serialize(payload);
                    comando.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value = json;
                }

                await using var lector = await comando.ExecuteReaderAsync();
                var conjuntos = new List<List<Dictionary<string, object?>>>();
                do
                {
                    if (lector.FieldCount == 0) continue;
                    var filas = new List<Dictionary<string, object?>>();
                    while (await lector.ReadAsync())
                    {
                        var fila = new Dictionary<string, object?>();
                        for (int i = 0; i < lector.FieldCount; i++)
                            fila[lector.GetName(i)] = await lector.IsDBNullAsync(i) ? null : lector.GetValue(i);
                        filas.Add(fila);
                    }
                    conjuntos.Add(filas);
                } while (await lector.NextResultAsync());

                // Un SELECT: lista simple; varios SELECT: todos los conjuntos, en orden.
                return Ok(conjuntos.Count == 1 ? (object)conjuntos[0] : conjuntos);
            }
            catch (SqlException ex)
            {
                // No exponer mensajes internos de SQL Server en producción.
                return StatusCode(500, new { exito = false, mensaje = "No se pudo completar la operación en la base de datos.", codigo = ex.Number });
            }
            catch (Exception)
            {
                return StatusCode(500, new { exito = false, mensaje = "Error interno al procesar la solicitud." });
            }
        }

        // GET api/Prestamo?estado=ACTIVO&idSolicitante=8
        [HttpGet]
        public Task<IActionResult> Listar([FromQuery] string? estado, [FromQuery] int? idSolicitante)
            => EjecutarSp("dbo.sp_prestamo_listar", new { datos = new { estado, idSolicitante } });

        // GET api/Prestamo/25/detalle
        [HttpGet("{id:int}/detalle")]
        public Task<IActionResult> ObtenerDetalle(int id)
            => EjecutarSp("dbo.sp_prestamo_obtener_detalle", new { datos = new { idPrestamo = id } });

        // GET api/Prestamo/solicitantes
        [HttpGet("solicitantes")]
        public Task<IActionResult> ListarSolicitantes()
            => EjecutarSp("dbo.sp_prestamo_listar_solicitantes_combo");

        // GET api/Prestamo/equipos-disponibles?idSolicitante=8&idLaboratorio=1
        [HttpGet("equipos-disponibles")]
        public Task<IActionResult> EquiposDisponibles([FromQuery] int? idSolicitante, [FromQuery] int? idLaboratorio)
            => EjecutarSp("dbo.sp_prestamo_listar_equipos_disponibles", new { datos = new { idSolicitante, idLaboratorio } });

        // GET api/Prestamo/equipos/qr/QR-PRUEBA-EQ-001?idSolicitante=8
        [HttpGet("equipos/qr/{codigoQR}")]
        public Task<IActionResult> BuscarPorQr(string codigoQR, [FromQuery] int idSolicitante)
            => EjecutarSp("dbo.sp_prestamo_buscar_equipo_por_qr", new { datos = new { codigoQR, idSolicitante } });

        // GET api/Prestamo/validar-permiso?idSolicitante=8&idTipoEquipo=1
        [HttpGet("validar-permiso")]
        public Task<IActionResult> ValidarPermiso([FromQuery] int idSolicitante, [FromQuery] int idTipoEquipo)
            => EjecutarSp("dbo.sp_prestamo_validar_permiso_solicitante", new { datos = new { idSolicitante, idTipoEquipo } });

        // POST api/Prestamo
        [HttpPost]
        public Task<IActionResult> Crear([FromBody] JsonElement body)
        {
            if (body.ValueKind != JsonValueKind.Object)
                return Task.FromResult<IActionResult>(BadRequest(new { exito = false, mensaje = "Se requiere un objeto JSON." }));
            return EjecutarSp("dbo.sp_prestamo_crear", body.GetRawText());
        }

        // PATCH api/Prestamo/25/anular
        [HttpPatch("{id:int}/anular")]
        public Task<IActionResult> Anular(int id, [FromBody] AnularPrestamoRequest body)
        {
            if (id <= 0 || body.IdUsuarioAccionante <= 0)
                return Task.FromResult<IActionResult>(BadRequest(new { exito = false, mensaje = "IDs inválidos." }));
            return EjecutarSp("dbo.sp_prestamo_anular", new
            {
                usuarioAccionante = new { idUsuario = body.IdUsuarioAccionante },
                datos = new { idPrestamo = id, observaciones = body.Observaciones }
            });
        }
    }

    public class AnularPrestamoRequest
    {
        public int IdUsuarioAccionante { get; set; }
        public string? Observaciones { get; set; }
    }
}
