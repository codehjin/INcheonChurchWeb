using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace INcheonChurchWeb.Tests;

/// <summary>AccountingService 생성자가 요구하는 최소 구현. 파일 접근은 쓰지 않는다.</summary>
public sealed class TestEnv : IWebHostEnvironment
{
    public string WebRootPath { get; set; } = Path.GetTempPath();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ApplicationName { get; set; } = "Tests";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public string EnvironmentName { get; set; } = "Testing";
}
