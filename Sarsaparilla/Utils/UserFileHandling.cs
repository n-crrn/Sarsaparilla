using System.IO;
using System.Text;

using Microsoft.JSInterop;

namespace Sarsaparilla.Utils;

public static class UserFileHandling
{

    private const string SaveFromStreamJSFunc = "saveFromStream";

    private static readonly UTF8Encoding Utf8 = new();

    public static async Task SaveStringToFile(IJSRuntime js, string filename, string data)
    {
        using MemoryStream memStream = new(Utf8.GetBytes(data));
        using DotNetStreamReference dnStreamRef = new(memStream);
        await js.InvokeVoidAsync(SaveFromStreamJSFunc, filename, dnStreamRef);
    }

}
