using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllOrigins", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Any, 5044);  // Ensure it listens on all network interfaces
});

var app = builder.Build();


app.UseCors("AllowAllOrigins");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();


app.MapGet("/convert", async (string? videoId, string? title, string? artist) =>
{
    if (string.IsNullOrEmpty(videoId))
    {
        return Results.BadRequest("Video ID cannot be blank");
    }

    try
    {
        Console.WriteLine("Starting conversion...");
        var url = $"https://www.youtube.com/watch?v={videoId}";
        var outputPath = Path.Combine(Path.GetTempPath(), $"{videoId}.mp3");

        // Prepare metadata options
        // Escape single quotes in title and artist
        string sanitizedTitle = title!.Replace("'", "'\\''");
        string sanitizedArtist = artist!.Replace("'", "'\\''");

        Console.WriteLine($"URL: {url}");
        Console.WriteLine($"Output Path: {outputPath}");
        Console.WriteLine($"Title Args: {title}");
        Console.WriteLine($"Artist Args: {artist}");

        // yt-dlp command
        var ytDlpPath = @"C:\yt-dlp\yt-dlp.exe";  // Adjust this path if needed
        var processInfo = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            Arguments = $"-x --audio-format mp3 --audio-quality 320K \"{url}\" -o \"{outputPath}\" --ffmpeg-location \"C:/ffmpeg/bin\" --postprocessor-args \"-metadata title='{sanitizedTitle}' -metadata artist='{sanitizedArtist}'\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process? process = null;
        try
        {
            process = Process.Start(processInfo);
            if (process == null)
            {
                return Results.BadRequest("Failed to start yt-dlp process.");
            }
        }
        catch (Exception ex)
        {
            return Results.Text($"Error starting process: {ex.Message}", "application/text", null, 500);
        }

        await process.WaitForExitAsync();
        Console.WriteLine($"Process exited with code {process.ExitCode}");

        if (process.ExitCode != 0)
        {
            var errorOutput = await process.StandardError.ReadToEndAsync();
            Console.WriteLine($"yt-dlp error output: {errorOutput}");
            return Results.BadRequest($"Error encountered: {errorOutput}");
        }

        Console.WriteLine("Conversion successful");

        var memory = new MemoryStream();
        await using (var stream = new FileStream(outputPath, FileMode.Open, FileAccess.Read))
        {
            await stream.CopyToAsync(memory);
        }

        memory.Position = 0;
        System.IO.File.Delete(outputPath);

        return Results.File(memory, "audio/mpeg", $"{title}.mp3");
    }
    catch (Exception e)
    {
        return Results.Text($"There was an error converting the YT video: {e.Message}", "application/text", null, 500);
    }
});

app.MapGet("/getinfo", async (string videoId) => {
    if (string.IsNullOrEmpty(videoId))
    {
        return Results.BadRequest("Video ID cannot be blank");
    }

    // yt-dlp command
    var url = $"https://www.youtube.com/watch?v={videoId}";
    var ytDlpPath = @"C:\yt-dlp\yt-dlp.exe";
    var processInfo = new ProcessStartInfo
    {
        FileName = ytDlpPath,
        Arguments = $"-j {url}",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    Process? process = null;
    try
    {
        process = Process.Start(processInfo);
        if (process == null)
        {
            return Results.BadRequest("Failed to start yt-dlp process.");
        }
    }
    catch (Exception ex)
    {
        return Results.Text($"Error starting process: {ex.Message}", "application/text", null, 500);
    }

    string output = await process.StandardOutput.ReadToEndAsync();
    string error = await process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();

    Console.WriteLine($"Process exited with code {process.ExitCode}");

    if (process.ExitCode != 0)
    {
        var errorOutput = await process.StandardError.ReadToEndAsync();
        Console.WriteLine($"yt-dlp error output: {errorOutput}");
        return Results.BadRequest($"Error encountered: {errorOutput}");
    }

    var metadata = JsonSerializer.Deserialize<JsonElement>(output);

    return Results.Json(metadata);

});




app.Run();
