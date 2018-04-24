namespace zipkin4net.dotnetcore
{
    public interface IServerTraceFactory
    {
        ServerTrace Create(string serviceName, string rpc);
    }
}
