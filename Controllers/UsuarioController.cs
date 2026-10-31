using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;

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

        // Mismo algoritmo SHA-256 hexadecimal que utiliza AuthController.
        private string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

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
        // Recibe datos.passwordHash como CONTRASENA EN TEXTO PLANO.
        // Reemplaza ese campo por un hash SHA-256 antes de llamar al SP.
        [HttpPost]
        public async Task<IActionResult> Crear([FromBody] JsonElement body)
        {
            if (!EsObjetoJson(body))
                return BadRequest(new { exito = false, mensaje = "Se requiere un objeto JSON." });
            try
            {
                var json = JsonNode.Parse(body.GetRawText())!.AsObject();
                if (json["datos"] is not JsonObject datos)
                    return BadRequest(new { exito = false, mensaje = "Debe enviar el objeto datos." });

                if (datos["passwordHash"] is not JsonValue passwordNode ||
                    !passwordNode.TryGetValue<string>(out var password) ||
                    string.IsNullOrWhiteSpace(password))
                    return BadRequest(new { exito = false, mensaje = "Debe enviar datos.passwordHash con la contrasena." });

                datos["passwordHash"] = HashPassword(password);
                var conjuntos = await EjecutarSpAsync("dbo.sp_usuario_crear", json.ToJsonString());
                return Ok(new { exito = true, mensaje = "Procedimiento de creación ejecutado.", datos = conjuntos });
            }
            catch (SqlException ex) { return RespuestaErrorSql(ex); }
            catch (JsonException) { return BadRequest(new { exito = false, mensaje = "Formato JSON incorrecto." }); }
            catch (Exception) { return StatusCode(500, new { exito = false, mensaje = "Error interno al crear usuario." }); }
        }

        // PUT api/Usuario
        // Si datos.passwordHash es null o no existe, conserva el hash en BD.
        // Si se envia un valor, lo interpreta como contrasena y genera SHA-256.
        [HttpPut]
        public async Task<IActionResult> Actualizar([FromBody] JsonElement body)
        {
            if (!EsObjetoJson(body))
                return BadRequest(new { exito = false, mensaje = "Se requiere un objeto JSON." });
            try
            {
                var json = JsonNode.Parse(body.GetRawText())!.AsObject();
                if (json["datos"] is not JsonObject datos)
                    return BadRequest(new { exito = false, mensaje = "Debe enviar el objeto datos." });

                if (datos.TryGetPropertyValue("passwordHash", out JsonNode? passwordNode) && passwordNode != null)
                {
                    if (passwordNode is not JsonValue value ||
                        !value.TryGetValue<string>(out var password) ||
                        string.IsNullOrWhiteSpace(password))
                        return BadRequest(new { exito = false, mensaje = "datos.passwordHash debe contener una contrasena valida o null." });

                    datos["passwordHash"] = HashPassword(password);
                }

                var conjuntos = await EjecutarSpAsync("dbo.sp_Usuario_Actualizar", json.ToJsonString());
                return Ok(new { exito = true, mensaje = "Procedimiento de actualización ejecutado.", datos = conjuntos });
            }
            catch (SqlException ex) { return RespuestaErrorSql(ex); }
            catch (JsonException) { return BadRequest(new { exito = false, mensaje = "Formato JSON incorrecto." }); }
            catch (Exception) { return StatusCode(500, new { exito = false, mensaje = "Error interno al actualizar usuario." }); }
        }

        // PATCH api/Usuario/7/estado
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

        // GET api/Usuario/12/biometria/estado
        [HttpGet("{id:int}/biometria/estado")]
        public async Task<IActionResult> EstadoBiometria(int id)
        {
            if (id <= 0) return BadRequest(new { exito = false, mensaje = "ID de usuario inválido." });
            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(
                    "SELECT COUNT(1) FROM dbo.BiometriaUsuario WHERE id_usuario = @id AND activo = 1", conn);
                cmd.Parameters.Add("@id", SqlDbType.Int).Value = id;
                var n = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                return Ok(new { exito = true, tiene_biometria = n > 0 });
            }
            catch (SqlException ex) { return RespuestaErrorSql(ex); }
            catch (Exception) { return StatusCode(500, new { exito = false, mensaje = "Error interno al consultar biometria." }); }
        }

        // POST api/Usuario/biometria/registrar
        [HttpPost("biometria/registrar")]
        public async Task<IActionResult> RegistrarBiometria([FromBody] JsonElement body)
        {
            if (!EsObjetoJson(body)) return BadRequest(new { exito = false, mensaje = "Se requiere un objeto JSON." });
            try
            {
                var conjuntos = await EjecutarSpAsync("dbo.sp_biometria_registrar", body.GetRawText());
                return Ok(new { exito = true, mensaje = "Biometria registrada correctamente.", datos = conjuntos });
            }
            catch (SqlException ex) { return RespuestaErrorSql(ex); }
            catch (Exception) { return StatusCode(500, new { exito = false, mensaje = "Error interno al registrar biometria." }); }
        }

        // POST api/Usuario/biometria/validar
        [HttpPost("biometria/validar")]
        public async Task<IActionResult> ValidarBiometria([FromBody] JsonElement body)
        {
            if (!EsObjetoJson(body)) return BadRequest(new { exito = false, mensaje = "Se requiere un objeto JSON." });
            try
            {
                var conjuntos = await EjecutarSpAsync("dbo.sp_biometria_validar", body.GetRawText());
                return Ok(new { exito = true, es_valido = true, datos = conjuntos });
            }
            catch (SqlException ex) { return RespuestaErrorSql(ex); }
            catch (Exception) { return StatusCode(500, new { exito = false, mensaje = "Error interno al validar biometria." }); }
        }
    }
}
