using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using LocationIngestor.Models;
using LocationIngestor.Services;

var builder = WebApplication.CreateBuilder(args);

// Bind to PORT (Render) or fallback to 5088 locally
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}
else
{
    builder.WebHost.UseUrls("http://localhost:5088");
}

builder.Services.AddSingleton<TextStorageService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// Addlogging
builder.Services.AddLogging(logging => logging.AddConsole());
// CORS abierto (en prod, restringe dominios específicos)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .SetPreflightMaxAge(TimeSpan.FromHours(1)));
});

var app = builder.Build();
app.UseCors("AllowAll");

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/", () => Results.Redirect("/swagger", permanent: false));
app.MapMethods("/api/locations", new[] { "OPTIONS" }, () => Results.Ok());

app.MapPost("/api/locations", ([FromBody] LocationBatch request, TextStorageService storage) =>
{
    if (request is null || request.locations is null || request.locations.Count == 0)
        return Results.BadRequest(new { error = "Debe enviar al menos un elemento en 'locations'." });

    var results = new List<object>();
    int ok = 0, fail = 0;

    foreach (var item in request.locations)
    {
        if (TryNormalize(item, out var norm, out var error))
        {
            storage.Append(norm);
            ok++;
            results.Add(new { alias = item.lm_device_alias, status = "ok" });
        }
        else
        {
            fail++;
            results.Add(new { alias = item.lm_device_alias, status = "error", message = error });
        }
    }

    return Results.Ok(new
    {
      received = request.locations.Count,
        ok,
        fail,
        results
    });
})
.WithName("PostLocations")
.Produces(200)
.Produces(400);

app.MapGet("/api/logs/today", (TextStorageService storage) =>
{
    try{
        var path = storage.GetTodayLogPath();
        if (!System.IO.File.Exists(path))
            return Results.NotFound(new{ error = "No hay archivo para hoy.", path = path });
        

        var txt = System.IO.File.ReadAllText(path);
        return Results.Text(txt, "text/plain");
    }
   catch (UnauthorizedAccessException ex)
    {
        return Results.Problem(new ProblemDetails
        {
            Title = "Acceso denegado al archivo",
            Detail = $"No se puede acceder al archivo de log: {ex.Message}",
            Status = 500
        });
    }
    catch (DirectoryNotFoundException ex)
    {
        return Results.Problem(new ProblemDetails
        {
            Title = "Directorio no encontrado",
            Detail= $"El directorio de logs no existe: {ex.Message}",
            Status = 500
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(new ProblemDetails
        {
            Title ="Erroral leer el archivo de log",
            Detail = $"Ocurrió un erroral intentar leer el archivo: {ex.Message}",
            Status = 500
        });
    }
})
.WithName("GetTodayLog");

app.Run();

static bool TryNormalize(LocationItem item, out NormalizedLocation norm, out string error)
{
    norm = null!;
    error = string.Empty;

   if (item is null)
    {
        error = "Elemento nulo";
        return false;
    }

    if (!double.TryParse(item.lm_latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
    {
        error ="Latitud inválida";
        return false;
    }
    if (!double.TryParse(item.lm_longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
    {
        error = "Longitud inválida";
       return false;
    }
    if (lat < -90 || lat > 90 || lon < -180 || lon > 180)
   {
        error = "Coordenadas fuera de rango";
        return false;
    }

    // Parse datetime
    var formats = new[]
{
        "yyyy-MM-dd HH:mm:ss.FFF",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy/MM/dd HH:mm:ss.FFF",
        "yyyy/MM/dd HH:mm:ss"
    };

    DateTime parsed;
    if (!DateTime.TryParseExact(item.lm_datetime, formats, CultureInfo.InvariantCulture,
           DateTimeStyles.None, out parsed))
    {
        if (!DateTime.TryParse(item.lm_datetime,CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
        {
            error ="Fecha/hora inválida";
            return false;
        }
    }

    // Convert to local (Ecuador) and UTC
   var tz = ResolveEcuadorTimeZone();
    var unspecified = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
    var local = TimeZoneInfo.ConvertTime(unspecified, tz);
   var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);

    norm = new NormalizedLocation
    {
        device_id = (item.lm_device_id ?? string.Empty).Trim(),
        alias = (item.lm_device_alias?? string.Empty).Trim(),
        latitude = lat,
        longitude = lon,
       dt_local = local,
        dt_utc = utc,
        dt_original = item.lm_datetime?.Trim() ?? ""
    };
    return true;
}

static TimeZoneInfo ResolveEcuadorTimeZone()
{
    try { return TimeZoneInfo.FindSystemTimeZoneById("America/Guayaquil"); }
    catch
    {
        try{ return TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time"); }
        catch { return TimeZoneInfo.Local; }
    }
}

namespace LocationIngestor.Models
{
    public class LocationBatch
    {
        public List<LocationItem> locations { get; set; } = new();
    }

    public class LocationItem
    {
        public string lm_device_id { get; set; }
        public string lm_latitude { get; set; }
        public string lm_longitude { get; set; }
        public string lm_device_alias { get; set; }
        public string lm_datetime { get; set; }
    }

    public class NormalizedLocation
    {
        public string device_id { get; set; }
        public string alias { get; set; }
       public double latitude { get; set; }
        public double longitude { get; set; }
public DateTime dt_local { get; set; }
        public DateTime dt_utc { get; set; }
        public string dt_original { get; set; }
    }
}

namespace LocationIngestor.Services
{
    public class TextStorageService
    {
        private readonly string _baseDir;
        private readonly ILogger<TextStorageService> _logger;

        public TextStorageService(IWebHostEnvironment env, IConfiguration config, ILogger<TextStorageService> logger)
        {
            _logger = logger;
            var cfg = config["Storage:BaseDir"];
           _baseDir = string.IsNullOrWhiteSpace(cfg)
                ? Path.Combine(env.ContentRootPath, "App_Data")
                : cfg;

            try
            {
                if (!Directory.Exists(_baseDir))
                {
                    _logger.LogInformation("Creating directory: {Directory}", _baseDir);
                    Directory.CreateDirectory(_baseDir);
                }
_logger.LogInformation("Base directory set to: {Directory}", _baseDir);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create oraccess base directory: {Directory}", _baseDir);
                throw;
            }
        }

        public void Append(NormalizedLocation loc)
        {
            try
            {
                var filePath = GetTodayLogPath();

                var lineObj = new
                {
                    device_id = loc.device_id,
                    alias = loc.alias,
                    latitude = loc.latitude,
                    longitude = loc.longitude,
                    dt_local = loc.dt_local.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    dt_utc = loc.dt_utc.ToUniversalTime().ToString("o"),
                    dt_original = loc.dt_original,
                    received_at_utc = DateTime.UtcNow.ToString("o")
                };

                var jsonLine = JsonSerializer.Serialize(lineObj);
                File.AppendAllText(filePath, jsonLine + Environment.NewLine);
           }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to append to log file");
                throw;
            }
        }

        public string GetTodayLogPath()
{
            try
            {
                var name = $"locations-{DateTime.Now:yyyyMMdd}.log";
                var path = Path.Combine(_baseDir, name);
                
                _logger.LogInformation("Getting log file path: {Path}", path);
                
                if (!File.Exists(path))
                {
                    _logger.LogInformation("Creatinglog file: {Path}", path);
                    Directory.CreateDirectory(_baseDir);
                    using var _ = File.Create(path);
                }
               return path;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get or create log file path");
                throw;
            }
        }
    }
}