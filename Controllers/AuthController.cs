using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SigelabBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly string _connectionString;

        public AuthController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("SqlServerConn")!;
        }

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

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginModel model)
        {
            try
            {
                string passHash = HashPassword(model.Password);

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"SELECT id_usuario, nombre, apellido, usuario, activo 
                                     FROM dbo.Usuario 
                                     WHERE usuario = @usuario AND password_hash = @passHash";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@usuario", model.Usuario);
                        command.Parameters.AddWithValue("@passHash", passHash);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                var usuarioData = new
                                {
                                    id_usuario = reader["id_usuario"],
                                    nombre = reader["nombre"].ToString(),
                                    apellido = reader["apellido"].ToString(),
                                    usuario = reader["usuario"].ToString(),
                                    activo = reader["activo"]
                                };

                                return Ok(new { exito = true, mensaje = "Bienvenido a SIGELAB", datos = JsonSerializer.Serialize(usuarioData) });
                            }
                        }
                    }
                }

                return Ok(new { exito = false, mensaje = "Usuario o contraseña incorrectos." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { exito = false, mensaje = $"Error en el servidor: {ex.Message}" });
            }
        }

        [HttpPost("registro")]
        public async Task<IActionResult> Registro([FromBody] RegistroModel model)
        {
            try
            {
                string passHash = HashPassword(model.Password);

                using (var connection = new SqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    string query = @"INSERT INTO dbo.Usuario 
                                     (id_rol, id_tipo_solicitante, nombre, apellido, codigo_personal, correo, usuario, password_hash, activo, fecha_creacion)
                                     VALUES 
                                     (@idRol, @idTipoSolicitante, @nombre, @apellido, @codigoPersonal, @correo, @usuario, @passHash, 1, GETDATE())";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@idRol", model.IdRol);
                        command.Parameters.AddWithValue("@idTipoSolicitante", model.IdTipoSolicitante);
                        command.Parameters.AddWithValue("@nombre", model.Nombre);
                        command.Parameters.AddWithValue("@apellido", model.Apellido);
                        command.Parameters.AddWithValue("@codigoPersonal", model.CodigoPersonal);
                        command.Parameters.AddWithValue("@correo", model.Correo);
                        command.Parameters.AddWithValue("@usuario", model.Usuario);
                        command.Parameters.AddWithValue("@passHash", passHash);

                        int filas = await command.ExecuteNonQueryAsync();

                        if (filas > 0)
                        {
                            return Ok(new { exito = true, mensaje = "Usuario registrado exitosamente." });
                        }
                    }
                }

                return Ok(new { exito = false, mensaje = "No se pudo registrar el usuario." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { exito = false, mensaje = $"Error en el servidor: {ex.Message}" });
            }
        }
    }

    public class LoginModel
    {
        public string Usuario { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class RegistroModel
    {
        public string Nombre { get; set; } = string.Empty;
        public string Apellido { get; set; } = string.Empty;
        public string CodigoPersonal { get; set; } = string.Empty;
        public string Correo { get; set; } = string.Empty;
        public string Usuario { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public int IdRol { get; set; } = 1;
        public int IdTipoSolicitante { get; set; } = 1;
    }
}