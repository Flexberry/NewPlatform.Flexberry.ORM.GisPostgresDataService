namespace NewPlatform.Flexberry.ORM
{
    using System;
    using System.Linq;
    using System.Text;

    using ICSSoft.STORMNET.Business;
    using ICSSoft.STORMNET.Business.Audit;
    using ICSSoft.STORMNET.Business.LINQProvider.Extensions;
    using ICSSoft.STORMNET.FunctionalLanguage;
    using ICSSoft.STORMNET.FunctionalLanguage.SQLWhere;
    using ICSSoft.STORMNET.Security;
    using ICSSoft.STORMNET.Windows.Forms;

    using Microsoft.Spatial;

    using STORMDO = ICSSoft.STORMNET;

    /// <summary>
    /// Сервис данных для работы с объектами ORM для Gis в PostgreSQL + PostGIS.
    /// </summary>
    public class GisPostgresDataService : PostgresDataService
    {
        private const string SqlGeographyTypecast = "::geography";
        private const string SqlGeometryTypecast = "::geometry";

        /// <summary>
        /// Создание сервиса данных для PostgreSQL без параметров.
        /// </summary>
        public GisPostgresDataService()
        {
        }

        /// <summary>
        /// Создание сервиса данных для PostgreSQL с указанием настроек проверки полномочий.
        /// </summary>
        /// <param name="securityManager">Сконструированный менеджер полномочий.</param>
        public GisPostgresDataService(ISecurityManager securityManager)
            : base(securityManager)
        {
        }

        /// <summary>
        /// Создание сервиса данных для PostgreSQL с указанием настроек проверки полномочий.
        /// </summary>
        /// <param name="securityManager">Сенеджер полномочий.</param>
        /// <param name="auditService">Сервис аудита.</param>
        public GisPostgresDataService(ISecurityManager securityManager, IAuditService auditService)
            : base(securityManager, auditService)
        {
        }

        /// <summary>
        /// Этот метод переопределён, чтобы подключить правильную подготовку гео-данных в запросе.
        /// </summary>
        /// <param name="customizationStruct">
        /// The customization struct.
        /// </param>
        /// <param name="ForReadValues">
        /// The for read values.
        /// </param>
        /// <param name="StorageStruct">
        /// The storage struct.
        /// </param>
        /// <param name="Optimized">
        /// The optimized.
        /// </param>
        /// <returns>
        /// The <see cref="string"/>.
        /// </returns>
        public override string GenerateSQLSelect(
            LoadingCustomizationStruct customizationStruct,
            // ReSharper disable once InconsistentNaming
            bool ForReadValues,
            // ReSharper disable once InconsistentNaming
            out StorageStructForView[] StorageStruct,
            // ReSharper disable once InconsistentNaming
            bool Optimized)
        {
            var sql = base.GenerateSQLSelect(customizationStruct, ForReadValues, out StorageStruct, Optimized);
            var fromPos = sql.IndexOf("FROM (");
            StringBuilder selectClause = new StringBuilder();
            STORMDO.View dataObjectView = customizationStruct.View;
            System.Type[] dataObjectType = customizationStruct.LoadingTypes;
            int lastPos = 0;
            for (int i = 0; i < dataObjectView.Properties.Length; i++)
            {
                var prop = dataObjectView.Properties[i];
                StorageStructForView.PropStorage propStorage = null;
                foreach (var storage in StorageStruct)
                {
                    propStorage = storage.props.FirstOrDefault(p => p.Name == prop.Name);
                    if (propStorage != null && propStorage.Name == prop.Name)
                        break;
                }
                if (propStorage == null || propStorage.propertyType != typeof(Geography) && propStorage.propertyType != typeof(Geometry))
                    continue;
                var propName = PutIdentifierIntoBrackets(prop.Name, true);
                var scanText = $"{propName},";
                int pos = sql.IndexOf(scanText, lastPos);
                if (pos == -1)
                {
                    scanText = $"{propName}{Environment.NewLine}";
                    pos = sql.IndexOf(scanText, lastPos);
                }
                if (pos == -1)
                    throw new ArgumentException($"Unexpected property name {propName}. Mismatch customizationStruct.View and SELECT clause.");
                if (pos > lastPos)
                {
                    selectClause.Append(sql.Substring(lastPos, pos - lastPos));
                }

                // The SQL-expression returns EWKT representation of the property value.
                selectClause.Append(sql.Substring(pos, scanText.Length).Replace(propName, $"ST_AsEWKT({propName}) as {propName}"));
                lastPos = pos + scanText.Length;
            }
            if (lastPos < fromPos)
            {
                selectClause.Append(sql.Substring(lastPos, fromPos - lastPos));
            }
            sql = $"{selectClause.ToString()}{sql.Substring(fromPos)}";
            return sql;
        }

        /// <summary>
        /// Осуществляет конвертацию заданного значения в строку запроса.
        /// </summary>
        /// <param name="value">Значение для конвертации.</param>
        /// <returns>Строка запроса.</returns>
        public override string ConvertValueToQueryValueString(object value)
        {
            // Assume the value is always stored as geometry.
            return ConvertValue(value, true);
        }

        /// <summary>
        /// Осуществляет преобразование заданного значения в SQL-строку.
        /// </summary>
        /// <param name="sqlLangDef">Определение языка ограничений.</param>
        /// <param name="value">Ограничивающая функция.</param>
        /// <param name="convertValue">Делегат для преобразования констант.</param>
        /// <param name="convertIdentifier">Делегат для преобразования идентификаторов.</param>
        /// <returns>Результирующая SQL-строка.</returns>
        public override string FunctionToSql(
            SQLWhereLanguageDef sqlLangDef,
            Function value,
            delegateConvertValueToQueryValueString convertValue,
            delegatePutIdentifierToBrackets convertIdentifier)
        {
            const string SqlDistanceFunction = "ST_Distance";
            const string SqlIntersectsFunction = "ST_Intersects";

            if (sqlLangDef == null)
            {
                throw new ArgumentNullException(nameof(sqlLangDef));
            }

            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            ExternalLangDef langDef = sqlLangDef as ExternalLangDef;

            var sqlFunction = string.Empty;
            if (value.FunctionDef.StringedView == langDef.funcGeoDistance || value.FunctionDef.StringedView == langDef.funcGeomDistance)
            {
                sqlFunction = SqlDistanceFunction;
            }
            else if (value.FunctionDef.StringedView == langDef.funcGeoIntersects || value.FunctionDef.StringedView == langDef.funcGeomIntersects)
            {
                sqlFunction = SqlIntersectsFunction;
            }

            if (!string.IsNullOrEmpty(sqlFunction))
            {
                var sqlTypecast = string.Empty;
                var convertGeographyToGeometry = true;
                if (value.FunctionDef.StringedView == langDef.funcGeoDistance || value.FunctionDef.StringedView == langDef.funcGeoIntersects)
                {
                    // Assume the return value of SQL-expression for identifier is geometry and needs typecast.
                    sqlTypecast = SqlGeographyTypecast;
                    convertGeographyToGeometry = false;
                }

                var sqlParameters = new string[2];
                sqlParameters[0] = value.Parameters[0] is VariableDef vd0
                    ? $"{PutIdentifierIntoBrackets(vd0.StringedView, true)}{sqlTypecast}"
                    : ConvertValue(value.Parameters[0], convertGeographyToGeometry);
                sqlParameters[1] = value.Parameters[1] is VariableDef vd1
                    ? $"{PutIdentifierIntoBrackets(vd1.StringedView, true)}{sqlTypecast}"
                    : ConvertValue(value.Parameters[1], convertGeographyToGeometry);

                return $"{sqlFunction}({sqlParameters[0]},{sqlParameters[1]})";
            }

            return base.FunctionToSql(sqlLangDef, value, convertValue, convertIdentifier);
        }

        private string ConvertValue(object value, bool convertGeographyToGeometry)
        {
            if (value is Geography geo)
            {
                return $"'{geo.GetEWKT()}'{(convertGeographyToGeometry ? SqlGeometryTypecast : SqlGeographyTypecast)}";
            }

            if (value is Geometry geom)
            {
                return $"'{geom.GetEWKT()}'{SqlGeometryTypecast}";
            }

            return base.ConvertValueToQueryValueString(value);
        }
    }
}
