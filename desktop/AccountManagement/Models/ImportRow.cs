using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountManagement.Models;

public sealed class ImportRow : ObservableObject
{
    public int RowNumber { get; init; }
    public string Platform { get; init; } = "";
    public string Url { get; init; } = "";
    public string Name { get; init; } = "";
    public string Login { get; init; } = "";
    internal string Password { get; set; } = "";
    public string PasswordSummary { get; init; } = "空密码";
    public string Notes { get; init; } = "";
    public string Tags { get; init; } = "";
    public string[] TagNames => Tags.Split([';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToArray();
    public bool Valid { get; set; } = true;
    private string state = "待导入", detail = "";
    public string State { get => state; set => SetProperty(ref state, value); }
    public string Detail { get => detail; set => SetProperty(ref detail, value); }
}

public sealed record ImportResponse(string Status, string? Id);
