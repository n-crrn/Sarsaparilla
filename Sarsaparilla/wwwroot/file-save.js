/*
 * JavaScript functions required to allow the user to save data to file.
 * This was written based on the guidance given at
 * https://docs.microsoft.com/en-us/aspnet/core/blazor/file-downloads?view=aspnetcore-6.0 .
 */

async function saveFromStream(filename, stream) {
    // Create a URL pointing to a blob containing the stream contents.
    const dataUrl = URL.createObjectURL(new Blob([await stream.arrayBuffer()]));
    // Use an anchor to trigger the download.
    const anchor = document.createElement('a');
    anchor.href = dataUrl;
    anchor.download = filename;
    anchor.click();
    // Dispose of temporary objects.
    anchor.remove();
    URL.revokeObjectURL(dataUrl);
}
