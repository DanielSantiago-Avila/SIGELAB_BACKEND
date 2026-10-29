using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

namespace SigelabBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsuarioController : ControllerBase
    {
        private readonly string _connectionString;

        public UsuarioController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("SqlServerConn")
                ?? configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Falta configurar SqlServerConn o DefaultConnection.");
        }

        // Devuelve todos los conjuntos de resultados que entregue el procedimiento.
        // Evita depender de columnas concretas o de que el SP retorne una columna 'mensaje'.
        private async Task<List<List<Dictionary<string, object?>>>> EjecutarSpAsync(
            string nombreSp, string? json = null)
        {
            var conjuntos = new List<List<Dictionary<string, object?>>>();
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(nombreSp, conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 30
            };
            if (json != null)
                cmd.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value = json;

            await using var reader = await cmd.ExecuteReaderAsync();
            do
            {
                if (reader.FieldCount == 0) continue;
                var filas = new List<Dictionary<string, object?>>();
                while (await reader.ReadAsync())
                {
                    var fila = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < reader.FieldCount; i++)
                        fila[reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
                    filas.Add(fila);
                }
                conjuntos.Add(filas);
            } while (await reader.NextResultAsync());
            return conjuntos;
        }

        private static bool EsObjetoJson(JsonElement body) => body.ValueKind == JsonValueKind.Object;

        private static IActionResult RespuestaErrorSql(SqlException ex)
        {
            // No exponer errores SQL internos al cliente.
            if (ex.Number >= 50000)
                return new BadRequestObjectResult(new { exito = false, mensaje = ex.Message });
            return new ObjectResult(new { exito = false, mensaje = "Error al consultar la base de datos." })
            { StatusCode = 500 };
        }

        // GET api/Usuario
        [HttpGet]
        public async Task<IActionResult> Listar()
        {
            try
            {
                var conjuntos = await EjecutarSpAsync("dbo.sp_usuario_listar");
                return Ok(conjuntos.Count > 0 ? conjuntos[0] : new List<Dictionary<string, object?>>());
            }
            catch (SqlException ex) { return RespuestaErrorSql(ex); }
            catch (Exception) { return StatusCode(500, new { exito = false, mensaje = "Error interno al listar usuarios." }); }
        }

        // GET api/Usuario/roles
        [HttpGet("roles")]
        public async Task<IActionResult> ListarRoles()
        {
            try
            {
                var conjuntos = await EjecutarSpAsync("dbo.sp_usuario_listar_roles");
                return Ok(conjuntos.Count > 0 ? conjuntos[0] : new List<Dictionary<string, object?>>());
            }
            catch (SqlException ex) { return RespuestaErrorSql(ex); }
            catch (Exception) { return StatusCode(500, new { exito = false, mensaje = "Error interno al listar roles." }); }
        }

        // GET api/Usuario/tipos-solicitante
        [HttpGet("tipos-solicitante")]
        public async Task<IActionResult> ListarTiposSolicitante()
        {
            try
            {
                var conjuntos = await EjecutarSpAsync("dbo.sp_usuario_listar_tipos_solicitante");
                return Ok(conjuntos.Count > 0 ? conjuntos[0] : new List<Dictionary<string, object?>>());
            }
            catch (SqlException ex) { return RespuestaErrorSql(ex); }
            catch (Exception) { return StatusCode(500, new { exito = false, mensaje = "Error interno al listar tipos de solicitante." }); }
        }

        // POST api/Usuario
        // Body: {"usuarioAccionante":{"idUsuario":6},"datos":{...}}
        [HttpPost]
        public async Task<IActionResult> Crear([FromBody] JsonElement body)
        {
            if (!EsObjetoJson(body)) return BadRequest(new { exito = false, mensaje = "Se requiere un objeto JSON." });
            try
            {
                var conjuntos = await EjecutarSpAsync("dbo.sp_usuario_crear", body.GetRawText());
                return Ok(new { exito = true, mensaje = "Procedimiento de creación ejecutado.", datos = conjuntos });
            }
            catch (SqlException ex) { return RespuestaErrorSql(ex); }
            catch (Exception) { return StatusCode(500, new { exito = false, mensaje = "Error interno al crear usuario." }); }
        }

        // PUT api/Usuario
        [HttpPut]
        public async Task<IActionResult> Actualizar([FromBody] JsonElement body)
        {
            if (!EsObjetoJson(body)) return BadRequest(new { exito = false, mensaje = "Se requiere un objeto JSON." });
            try
            {
                var conjuntos = await EjecutarSpAsync("dbo.sp_Usuario_Actualizar", body.GetRawText());
                return Ok(new { exito = true, mensaje = "Procedimiento de actualización ejecutado.", datos = conjuntos });
            }
            catch (SqlException ex) { return RespuestaErrorSql(ex); }
            catch (Exception) { return StatusCode(500, new { exito = false, mensaje = "Error interno al actualizar usuario." }); }
        }

        // PATCH api/Usuario/7/estado
        // Body: {"usuarioAccionante":{"idUsuario":6}}
        // El SP alterna activo/inactivo; NO acepta datos.activo.
        [HttpPatch("{id:int}/estado")]
        public async Task<IActionResult> CambiarEstado(int id, [FromBody] JsonElement body)
        {
            if (!EsObjetoJson(body) ||
                !body.TryGetProperty("usuarioAccionante", out var accionante) ||
                accionante.ValueKind != JsonValueKind.Object ||
                !accionante.TryGetProperty("idUsuario", out var actor) ||
                !actor.TryGetInt32(out int idActor) || idActor <= 0)
                return BadRequest(new { exito = false, mensaje = "Debe enviar usuarioAccionante.idUsuario." });
            if (id <= 0) return BadRequest(new { exito = false, mensaje = "ID de usuario inválido." });

            var json = JsonSerializer.Serialize(new
            {
                usuarioAccionante = new { idUsuario = idActor },
                datos = new { idUsuario = id }
            });
            try
            {
                var conjuntos = await EjecutarSpAsync("dbo.sp_usuario_cambiar_estado", json);
                return Ok(new { exito = true, mensaje = "Procedimiento de cambio de estado ejecutado.", datos = conjuntos });
            }
            catch (SqlException ex) { return RespuestaErrorSql(ex); }
            catch (Exception) { return StatusCode(500, new { exito = false, mensaje = "Error interno al cambiar estado." }); }
        }
    }
}
