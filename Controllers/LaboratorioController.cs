
var builder = WebApplication.CreateBuilder(args);

// Agregar servicios para los controladores
builder.Services.AddControllers();

// Validar que exista la cadena de conexión
var connectionString = builder.Configuration
    .GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "No se encontró la configuración ConnectionStrings:DefaultConnection. " +
        "Verifica el appsettings.json o las variables de entorno del servidor."
    );
}

// Configurar CORS para permitir peticiones desde Flutter
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFlutter",
        policy => policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());
});

// Construir la aplicación
var app = builder.Build();

// Activar middleware
app.UseCors("AllowFlutter");

app.UseAuthorization();

// Mapear controladores
app.MapControllers();

// Iniciar aplicación
app.Run();
