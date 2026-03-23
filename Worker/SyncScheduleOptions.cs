namespace Worker;

public class SyncScheduleOptions
{
    public bool TestMode { get; set; } = false;
    public int TestLoopIntervalMinutes { get; set; } = 2;
    public bool ShowLoopLogs { get; set; } = false;
    public int LoopIntervalMinutes { get; set; } = 5;
    public int CadastroHoraInicial { get; set; } = 21;
    public int EstoqueJanelaMinutos { get; set; } = 5;
    public int PrecoHora { get; set; } = 19;
    public int PrecoJanelaMinutos { get; set; } = 5;
    public int ExclusaoHora { get; set; } = 22;
    public int ExclusaoJanelaMinutos { get; set; } = 5;
    public int DadosHora { get; set; } = 23;
    public int DadosJanelaMinutos { get; set; } = 5;
    public int PedidosIntervalMinutes { get; set; } = 15;
    public int MaxParallelRequests { get; set; } = 3;
    public int DelayBetweenRequestsMs { get; set; } = 250;
    public int QueueBufferSize { get; set; } = 500;
    public int CadastroMaxParallelRequests { get; set; } = 1;
    public int CadastroDelayBetweenRequestsMs { get; set; } = 500;
    public int EstoqueMaxParallelRequests { get; set; } = 3;
    public int EstoqueDelayBetweenRequestsMs { get; set; } = 150;
    public int PrecoMaxParallelRequests { get; set; } = 2;
    public int PrecoDelayBetweenRequestsMs { get; set; } = 250;
    public int ExclusaoMaxParallelRequests { get; set; } = 1;
    public int ExclusaoDelayBetweenRequestsMs { get; set; } = 400;
    public int DadosMaxParallelRequests { get; set; } = 1;
    public int DadosDelayBetweenRequestsMs { get; set; } = 400;
    public int PedidosMaxParallelRequests { get; set; } = 1;
    public int PedidosDelayBetweenRequestsMs { get; set; } = 400;
}
