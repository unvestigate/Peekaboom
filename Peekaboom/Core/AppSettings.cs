using System.Text.Json;
using System.Text.Json.Serialization;

using Peekaboom.Rendering;

namespace Peekaboom.Core;

public sealed class AppSettings
{
    private const string APP_FOLDER = "Peekaboom";
    private const string FILE_NAME = "settings.json";

    public RenderMode RenderMode { get; set; } = RenderMode.Solid;

    public bool ShowGrid { get; set; } = true;

    public bool LeftHanded { get; set; }

    public bool PreSimulation { get; set; }

    public int ListenPort { get; set; } = 5425;

    public string LiveCaptureFolder { get; set; } = "";

    public int KeepLiveCaptures { get; set; } = 5;

    public double PlaybackSpeed { get; set; } = 1.0;

    public DisplayOptions Display { get; set; } = new();

    [JsonIgnore]
    public string ResolvedLiveCaptureFolder => LiveCaptureFolder.Length > 0
        ? LiveCaptureFolder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), APP_FOLDER, "Live");

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), APP_FOLDER, FILE_NAME);

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                AppSettings? settings = JsonSerializer.Deserialize(File.ReadAllText(FilePath), AppSettingsContext.Default.AppSettings);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException)
        {
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, AppSettingsContext.Default.AppSettings));
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsContext : JsonSerializerContext
{
}
