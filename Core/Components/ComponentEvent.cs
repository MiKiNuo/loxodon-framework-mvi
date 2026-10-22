namespace MVI.Components
{
    public sealed class ComponentEvent
    {
        public ComponentEvent(string componentId, string eventName, object payload)
        {
            ComponentId = componentId;
            EventName = eventName;
            Payload = payload;
        }

        // 组件 ID，用于路由和定位。
        public string ComponentId { get; }

        // 事件名，推荐使用稳定字符串常量。
        public string EventName { get; }

        // 事件数据载荷，业务层自行约束类型。
        public object Payload { get; }
    }
}
