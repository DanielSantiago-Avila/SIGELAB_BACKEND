using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

namespace SigelabBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LaboratorioController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly ILogger<LaboratorioController> _logger;

        public LaboratorioController(IConfiguration configuration, ILogger<LaboratorioController> logger)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("SqlServerConn")
                ?? configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("No existe una cadena de conexión SQL Server configurada.");
            if (string.IsNullOrWhiteSpace(_connectionString))
                throw new InvalidOperationException("La cadena de conexión SQL Server está vacía.");
        }

        private async Task<IActionResult> EjecutarAsync(string sp, object? payload = null)
        {
            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await using var command = new SqlCommand("dbo." + sp, connection);
                command.CommandType = CommandType.StoredProcedure;
                if (payload != null)
                {
                    string json = payload is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(payload);
                    command.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value = json;
                }
                await connection.OpenAsync();
                await using var reader = await command.ExecuteReaderAsync();
                var rows = new List<Dictionary<string, object?>>();
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
                    rows.Add(row);
                }
                // Los SP de escritura retornan una sola fila con exito, mensaje e id_laboratorio.
                if (sp is "sp_laboratorio_crear" or "sp_actualizar_laboratorio" or "sp_cambiar_estado_laboratorio")
                    return rows.Count > 0 ? Ok(rows[0]) : StatusCode(500, new { exito = false, mensaje = "El procedimiento no devolvió confirmación." });
                return Ok(rows);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error SQL en {StoredProcedure} (código {SqlNumber})", sp, ex.Number);
                return StatusCode(500, new { exito = false, mensaje = "Error al ejecutar la operación en la base de datos." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al ejecutar {StoredProcedure}", sp);
                return StatusCode(500, new { exito = false, mensaje = "Error interno del servidor." });
            }
        }

        // GET /api/Laboratorio?activo=true (sin filtro: todos)
        [HttpGet]
        public Task<IActionResult> ObtenerLaboratorios([FromQuery] bool? activo = null) =>
            EjecutarAsync("sp_obtener_laboratorio", new
            {
                usuarioAccionante = new { },
                datos = new { activo }
            });

        // GET /api/Laboratorio/activos-combo
        [HttpGet("activos-combo")]
        public Task<IActionResult> ListarActivosCombo() =>
            EjecutarAsync("sp_laboratorio_listar_activos_combo");

        // GET /api/Laboratorio/equipos-combo
        [HttpGet("equipos-combo")]
        public Task<IActionResult> ListarLaboratoriosParaEquipo() =>
            EjecutarAsync("sp_equipo_listar_laboratorios_combo");

        // POST /api/Laboratorio
        [HttpPost]
        public Task<IActionResult> CrearLaboratorio([FromBody] JsonElement json) =>
            EjecutarAsync("sp_laboratorio_crear", json);

        // PUT /api/Laboratorio
        [HttpPut]
        public Task<IActionResult> ActualizarLaboratorio([FromBody] JsonElement json) =>
            EjecutarAsync("sp_actualizar_laboratorio", json);

        // PATCH /api/Laboratorio/{id}/estado
        // Body: {"usuarioAccionante":{"idUsuario":5},"datos":{"activo":false}}
        [HttpPatch("{id:int}/estado")]
        public Task<IActionResult> CambiarEstado(int id, [FromBody] JsonElement json)
        {
            if (!json.TryGetProperty("usuarioAccionante", out var actor) ||
                !actor.TryGetProperty("idUsuario", out var idUsuario) ||
                !idUsuario.TryGetInt32(out int userId) || userId <= 0 ||
                !json.TryGetProperty("datos", out var datos) ||
                !datos.TryGetProperty("activo", out var activo) ||
                (activo.ValueKind != JsonValueKind.True && activo.ValueKind != JsonValueKind.False))
                return Task.FromResult<IActionResult>(BadRequest(new { exito = false, mensaje = "Se requiere usuarioAccionante.idUsuario y datos.activo booleano." }));
            return EjecutarAsync("sp_cambiar_estado_laboratorio", new
            {
                usuarioAccionante = new { idUsuario = userId },
                datos = new { id_laboratorio = id, activo = activo.GetBoolean() }
            });
        }
    }
}
