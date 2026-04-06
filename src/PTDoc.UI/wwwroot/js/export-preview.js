export async function downloadStreamFile(fileName, contentType, streamReference) {
    const arrayBuffer = await streamReference.arrayBuffer();
    const blob = new Blob([arrayBuffer], { type: contentType });
    const href = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.download = fileName;
    link.rel = "noopener";
    link.href = href;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(href);
}
