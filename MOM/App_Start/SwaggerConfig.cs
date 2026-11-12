using System.IO;
using System.Web.Hosting;
using System.Web.Http;
using Swashbuckle.Application;

namespace MOM
{
    /// <summary>
    /// Swagger 文档配置：访问 /swagger 查看并在线调试全部接口。
    /// </summary>
    public static class SwaggerConfig
    {
        public static void Register()
        {
            GlobalConfiguration.Configuration
                .EnableSwagger(c =>
                {
                    c.SingleApiVersion("v1", "MOM PDF 解析 API");

                    string xmlPath = HostingEnvironment.MapPath("~/bin/MOM.xml");
                    if (!string.IsNullOrEmpty(xmlPath) && File.Exists(xmlPath))
                    {
                        c.IncludeXmlComments(xmlPath);
                    }
                })
                .EnableSwaggerUi(c =>
                {
                    c.DocumentTitle("MOM PDF 解析 API");
                });
        }
    }
}
