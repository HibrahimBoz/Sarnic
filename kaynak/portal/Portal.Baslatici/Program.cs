// Atölye menüsünü simgeli bir exe ile açar. Paketin kök klasöründe durur.
using System.Diagnostics;

string kok = AppContext.BaseDirectory;
string betik = Path.Combine(kok, "SarnicAtolye", "araclar", "baslat.ps1");
if (!File.Exists(betik))
{
    Console.Error.WriteLine($"Menü bulunamadı: {betik}");
    Console.Error.WriteLine("Bu dosyayı SarnicAtolye klasörünün yanından çalıştırın. Enter ile çıkın.");
    Console.ReadLine();
    return 1;
}
Console.Title = "Sarnıç Atölye";
var bilgi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, WorkingDirectory = kok };
foreach (string a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", betik })
{
    bilgi.ArgumentList.Add(a);
}
using Process surec = Process.Start(bilgi)!;
surec.WaitForExit();
return surec.ExitCode;
