
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

namespace SigelabBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TipoEquipoController : ControllerBase
    {
        private readonly string _connectionString;

        public TipoEquipoController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "No se encontró DefaultConnection.");
        }

        private async Task<IActionResult> EjecutarSP(
            string nombreSP, JsonElement json)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                using var command = new SqlCommand(nombreSP, connection);

                command.CommandType = CommandType.StoredProcedure;
                command.Parameters.Add("@json", SqlDbType.NVarChar, -1)
                    .Value = json.GetRawText();

                await connection.OpenAsync();

                using var reader = await command.ExecuteReaderAsync();

                var resultados = new List<object>();

                do
                {
                    var filas = new List<Dictionary<string, object?>>();

                    while (await reader.ReadAsync())
                    {
                        var fila = new Dictionary<string, object?>();

                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            fila[reader.GetName(i)] =
                                await reader.IsDBNullAsync(i)
                                    ? null
                                    : reader.GetValue(i);
                        }

                        filas.Add(fila);
                    }

                    resultados.Add(filas);

                } while (await reader.NextResultAsync());

                return Ok(resultados);
            }
            catch (SqlException ex)
            {
                return StatusCode(500, new
                {
                    mensaje = "Error al ejecutar el procedimiento almacenado.",
                    detalle = ex.Message
                });
            }
            catch (Exception)
            {
                return StatusCode(500, new
                {
                    mensaje = "Error interno del servidor."
                });
            }
        }

        // 1. Listar todos los tipos de equipo
        [HttpGet("listar")]
        public Task<IActionResult> Listar()
        {
            return EjecutarSP(
                "dbo.sp_TipoEquipo_Listar",
                JsonSerializer.SerializeToElement(new { }));
        }

        // 2. Obtener tipo de equipo por ID
        [HttpGet("obtener/{idTipoEquipo:int}")]
        public Task<IActionResult> ObtenerPorId(int idTipoEquipo)
        {
            return EjecutarSP(
                "dbo.sp_TipoEquipo_ObtenerPorId",
                JsonSerializer.SerializeToElement(new
                {
                    idTipoEquipo
                }));
        }

        // 3. Listar activos para ComboBox
        [HttpGet("listar-activos-combo")]
        public Task<IActionResult> ListarActivosCombo()
        {
            return EjecutarSP(
                "dbo.sp_TipoEquipo_ListarActivosCombo",
                JsonSerializer.SerializeToElement(new { }));
        }

        // 4. Crear tipo de equipo
        [HttpPost("crear")]
        public Task<IActionResult> Crear([FromBody] JsonElement json)
        {
            return EjecutarSP("dbo.sp_TipoEquipo_Crear", json);
        }

        // 5. Actualizar tipo de equipo
        [HttpPut("actualizar")]
        public Task<IActionResult> Actualizar([FromBody] JsonElement json)
        {
            return EjecutarSP("dbo.sp_TipoEquipo_Actualizar", json);
        }

        // 6. Desactivar tipo de equipo
        [HttpPut("desactivar")]
        public Task<IActionResult> Desactivar([FromBody] JsonElement json)
        {
            return EjecutarSP("dbo.sp_TipoEquipo_Desactivar", json);
        }

        // 7. Activar tipo de equipo
        [HttpPut("activar")]
        public Task<IActionResult> Activar([FromBody] JsonElement json)
        {
            return EjecutarSP("dbo.sp_TipoEquipo_Activar", json);
        }
    }
}
