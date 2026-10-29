using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

namespace SigelabBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MantenimientoController : ControllerBase
    {
        private readonly string _connectionString;

        public MantenimientoController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("SqlServerConn")
                ?? configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Falta configurar SqlServerConn o DefaultConnection.");
        }

        // Lee todas las tablas de resultados que devuelva el SP, sin asumir columnas fijas.
        private async Task<object> EjecutarSpAsync(string nombreSp, object? datosJson = null)
        {
            await using var conexion = new SqlConnection(_connectionString);
            await conexion.OpenAsync();
            await using var comando = new SqlCommand(nombreSp, conexion)
            {
                CommandType = CommandType.StoredProcedure
            };

            if (datosJson != null)
            {
                var parametro = comando.Parameters.Add("@json", SqlDbType.NVarChar, -1);
                parametro.Value = datosJson is string texto ? texto : JsonSerializer.Serialize(datosJson);
            }

            var resultados = new List<List<Dictionary<string, object?>>>();
            await using var lector = await comando.ExecuteReaderAsync();
            do
            {
                if (lector.FieldCount == 0) continue;
                var filas = new List<Dictionary<string, object?>>();
                while (await lector.ReadAsync())
                {
                    var fila = new Dictionary<string, object?>();
                    for (int i = 0; i < lector.FieldCount; i++)
                    {
                        var nombre = lector.GetName(i);
                        if (fila.ContainsKey(nombre)) nombre = $"{nombre}_{i}";
                        fila[nombre] = await lector.IsDBNullAsync(i) ? null : lector.GetValue(i);
                    }
                    filas.Add(fila);
                }
                resultados.Add(filas);
            } while (await lector.NextResultAsync());

            return resultados.Count switch
            {
                0 => new { exito = true, mensaje = "Operación ejecutada correctamente." },
                1 => resultados[0],
                _ => resultados
            };
        }

        private IActionResult ErrorSql(SqlException ex)
        {
            // No exponer detalles internos de SQL Server al cliente.
            return StatusCode(500, new { exito = false, mensaje = "Error al ejecutar la operación en la base de datos.", codigo = ex.Number });
        }

        // GET api/Mantenimiento?estado=ABIERTO
        // GET api/Mantenimiento?estado=FINALIZADO
        [HttpGet]
        public async Task<IActionResult> Listar([FromQuery] string? estado = null)
        {
            try
            {
                object datos = string.IsNullOrWhiteSpace(estado)
                    ? new { }
                    : new { datos = new { estado } };
                return Ok(await EjecutarSpAsync("dbo.sp_mantenimiento_listar", datos));
            }
            catch (SqlException ex) { return ErrorSql(ex); }
        }

        // GET api/Mantenimiento/equipos?busqueda=DEMO26
        [HttpGet("equipos")]
        public async Task<IActionResult> ListarEquipos([FromQuery] string? busqueda = null)
        {
            try
            {
                object datos = string.IsNullOrWhiteSpace(busqueda)
                    ? new { }
                    : new { datos = new { busqueda } };
                return Ok(await EjecutarSpAsync("dbo.sp_mantenimiento_listar_equipos_para_mantenimiento", datos));
            }
            catch (SqlException ex) { return ErrorSql(ex); }
        }

        // GET api/Mantenimiento/equipos/1/historial
        [HttpGet("equipos/{id:int}/historial")]
        public async Task<IActionResult> HistorialEquipo(int id)
        {
            if (id <= 0) return BadRequest(new { exito = false, mensaje = "El ID del equipo debe ser positivo." });
            try
            {
                return Ok(await EjecutarSpAsync("dbo.sp_mantenimiento_obtener_historial_equipo",
                    new { datos = new { idEquipo = id } }));
            }
            catch (SqlException ex) { return ErrorSql(ex); }
        }

        // POST api/Mantenimiento/iniciar
        // Recibe el JSON completo: usuarioAccionante y datos.
        [HttpPost("iniciar")]
        public async Task<IActionResult> Iniciar([FromBody] JsonElement jsonBody)
        {
            if (jsonBody.ValueKind != JsonValueKind.Object)
                return BadRequest(new { exito = false, mensaje = "Se requiere un objeto JSON." });
            try
            {
                return Ok(await EjecutarSpAsync("dbo.sp_mantenimiento_iniciar", jsonBody.GetRawText()));
            }
            catch (SqlException ex) { return ErrorSql(ex); }
        }

        // PATCH api/Mantenimiento/1/finalizar
        // Body: { "usuarioAccionante": { "idUsuario": 6 },
        //         "datos": { "resultado": "REPARADO", "observaciones": "..." } }
        [HttpPatch("{id:int}/finalizar")]
        public async Task<IActionResult> Finalizar(int id, [FromBody] JsonElement jsonBody)
        {
            if (id <= 0) return BadRequest(new { exito = false, mensaje = "El ID del mantenimiento debe ser positivo." });
            if (jsonBody.ValueKind != JsonValueKind.Object ||
                !jsonBody.TryGetProperty("usuarioAccionante", out var accionante) ||
                accionante.ValueKind != JsonValueKind.Object ||
                !jsonBody.TryGetProperty("datos", out var datos) ||
                datos.ValueKind != JsonValueKind.Object)
            {
                return BadRequest(new { exito = false, mensaje = "Se requieren usuarioAccionante y datos." });
            }

            // Se toma el idMantenimiento de la URL para evitar discrepancias.
            var datosFinal = new Dictionary<string, object?>();
            foreach (var propiedad in datos.EnumerateObject())
            {
                if (!propiedad.NameEquals("idMantenimiento"))
                    datosFinal[propiedad.Name] = propiedad.Value.Clone();
            }
            datosFinal["idMantenimiento"] = id;

            try
            {
                var json = JsonSerializer.Serialize(new
                {
                    usuarioAccionante = accionante.Clone(),
                    datos = datosFinal
                });
                return Ok(await EjecutarSpAsync("dbo.sp_mantenimiento_finalizar", json));
            }
            catch (SqlException ex) { return ErrorSql(ex); }
        }
    }
}
