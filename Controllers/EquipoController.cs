using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

namespace SigelabBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EquipoController : ControllerBase
    {
        private readonly string _connectionString;

        public EquipoController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("SqlServerConn")
                ?? configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("No se configuró la conexión SQL Server.");
        }

        private async Task<List<Dictionary<string, object?>>> ConsultarAsync(
            string procedimiento, string? json = null)
        {
            var registros = new List<Dictionary<string, object?>>();
            await using var conexion = new SqlConnection(_connectionString);
            await conexion.OpenAsync();
            await using var comando = new SqlCommand(procedimiento, conexion)
            {
                CommandType = CommandType.StoredProcedure
            };
            if (json != null)
                comando.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value = json;

            await using var lector = await comando.ExecuteReaderAsync();
            while (await lector.ReadAsync())
            {
                var registro = new Dictionary<string, object?>();
                for (int i = 0; i < lector.FieldCount; i++)
                    registro[lector.GetName(i)] = await lector.IsDBNullAsync(i) ? null : lector.GetValue(i);
                registros.Add(registro);
            }
            return registros;
        }

        private async Task<List<Dictionary<string, object?>>> EjecutarAsync(
            string procedimiento, string json)
        {
            // Consume el primer result set si el SP devuelve información; si no, devuelve lista vacía.
            return await ConsultarAsync(procedimiento, json);
        }

        private IActionResult ErrorSql(SqlException ex)
        {
            // No exponer detalles internos de SQL Server al cliente.
            return StatusCode(500, new { exito = false, mensaje = "No fue posible completar la operación en la base de datos." });
        }

        // GET api/Equipo?estado=DISPONIBLE&activo=true&idLaboratorio=1
        [HttpGet]
        public async Task<IActionResult> Listar(
            [FromQuery] int? idEquipo, [FromQuery] int? idTipoEquipo,
            [FromQuery] int? idLaboratorio, [FromQuery] string? estado,
            [FromQuery] bool? activo, [FromQuery] string? codigoInventario,
            [FromQuery] string? busqueda)
        {
            try
            {
                var json = JsonSerializer.Serialize(new
                {
                    datos = new { idEquipo, idTipoEquipo, idLaboratorio, estado, activo, codigoInventario, busqueda }
                });
                return Ok(await ConsultarAsync("dbo.sp_equipo_listar", json));
            }
            catch (SqlException ex) { return ErrorSql(ex); }
        }

        [HttpGet("tipos")]
        public async Task<IActionResult> ListarTipos()
        {
            try { return Ok(await ConsultarAsync("dbo.sp_equipo_listar_tipos_combo")); }
            catch (SqlException ex) { return ErrorSql(ex); }
        }

        [HttpGet("laboratorios")]
        public async Task<IActionResult> ListarLaboratorios()
        {
            try { return Ok(await ConsultarAsync("dbo.sp_equipo_listar_laboratorios_combo")); }
            catch (SqlException ex) { return ErrorSql(ex); }
        }

        [HttpGet("qr/{codigoQR}")]
        public async Task<IActionResult> BuscarPorQr(string codigoQR)
        {
            try
            {
                var json = JsonSerializer.Serialize(new { datos = new { codigoQR } });
                var registros = await ConsultarAsync("dbo.sp_equipo_buscar_por_qr", json);
                return registros.Count == 0
                    ? NotFound(new { exito = false, mensaje = "Equipo no encontrado." })
                    : Ok(registros[0]);
            }
            catch (SqlException ex) { return ErrorSql(ex); }
        }

        [HttpPost]
        public async Task<IActionResult> Crear([FromBody] JsonElement cuerpo)
        {
            try
            {
                var registros = await EjecutarAsync("dbo.sp_equipo_crear", cuerpo.GetRawText());
                return Ok(new { exito = true, mensaje = "Procedimiento de creación ejecutado.", datos = registros });
            }
            catch (SqlException ex) { return ErrorSql(ex); }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Actualizar(int id, [FromBody] JsonElement cuerpo)
        {
            try
            {
                using var documento = JsonDocument.Parse(cuerpo.GetRawText());
                var raiz = documento.RootElement;
                if (!raiz.TryGetProperty("usuarioAccionante", out var actor) ||
                    !raiz.TryGetProperty("datos", out var datos) || datos.ValueKind != JsonValueKind.Object)
                    return BadRequest(new { exito = false, mensaje = "Se requieren usuarioAccionante y datos." });
                if (datos.TryGetProperty("idEquipo", out var idJson) &&
                    (idJson.ValueKind != JsonValueKind.Number || !idJson.TryGetInt32(out var idBody) || idBody != id))
                    return BadRequest(new { exito = false, mensaje = "El idEquipo del JSON no coincide con la URL." });

                var campos = new Dictionary<string, object?>();
                foreach (var propiedad in datos.EnumerateObject())
                    campos[propiedad.Name] = propiedad.Value.Clone();
                campos["idEquipo"] = id;
                var json = JsonSerializer.Serialize(new { usuarioAccionante = actor.Clone(), datos = campos });
                var registros = await EjecutarAsync("dbo.sp_equipo_actualizar", json);
                return Ok(new { exito = true, mensaje = "Procedimiento de actualización ejecutado.", datos = registros });
            }
            catch (JsonException) { return BadRequest(new { exito = false, mensaje = "JSON inválido." }); }
            catch (SqlException ex) { return ErrorSql(ex); }
        }

        [HttpPatch("{id:int}/estado")]
        public async Task<IActionResult> CambiarEstado(int id, [FromBody] CambiarEstadoEquipoRequest solicitud)
        {
            try
            {
                var json = JsonSerializer.Serialize(new
                {
                    usuarioAccionante = new { idUsuario = solicitud.IdUsuarioAccionante },
                    datos = new { idEquipo = id, activo = solicitud.Activo }
                });
                var registros = await EjecutarAsync("dbo.sp_equipo_cambiar_estado", json);
                return Ok(new { exito = true, mensaje = "Procedimiento de cambio de estado ejecutado.", datos = registros });
            }
            catch (SqlException ex) { return ErrorSql(ex); }
        }
    }

    public class CambiarEstadoEquipoRequest
    {
        public int IdUsuarioAccionante { get; set; }
        public bool Activo { get; set; }
    }
}
