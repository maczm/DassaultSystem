using System.Web.Http;
using Newtonsoft.Json;

namespace MOM
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // Web API 配置和服务
            // 返回压缩后的 JSON（不缩进、不换行）
            config.Formatters.JsonFormatter.SerializerSettings.Formatting = Formatting.None;

            // 只提供 JSON，去掉 XML 格式化器
            config.Formatters.Remove(config.Formatters.XmlFormatter);

            // Web API 路由
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );
        }
    }
}
