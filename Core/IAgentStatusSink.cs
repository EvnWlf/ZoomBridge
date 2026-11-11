namespace ZoomBridge.Core
{
    /// <summary>
    /// Canal por el que el motor informa su actividad. Mantiene el motor desacoplado de la UI:
    /// las pruebas pueden omitirlo y la app lo implementa con <c>AgentState</c>.
    /// </summary>
    public interface IAgentStatusSink
    {
        void ReportIdle();
        void ReportWaiting(string folderName);
        void ReportPrompting(string folderName);
        void ReportTransfer(string folderName, long copiedBytes, long totalBytes);
        void ReportError(string message);
        void Notify(string title, string message);
        void RecordingsChanged();
    }
}
