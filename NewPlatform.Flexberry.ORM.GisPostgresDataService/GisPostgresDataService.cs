﻿namespace NewPlatform.Flexberry.ORM
{
    using System;
    using System.Linq;
    using System.Text;

    using Microsoft.Spatial;

    using ICSSoft.STORMNET.Business;
    using ICSSoft.STORMNET.Business.Audit;
    using ICSSoft.STORMNET.Business.LINQProvider.Extensions;
    using ICSSoft.STORMNET.FunctionalLanguage;
    using ICSSoft.STORMNET.FunctionalLanguage.SQLWhere;
    using ICSSoft.STORMNET.Security;
    using ICSSoft.STORMNET.Windows.Forms;

    using STORMDO = ICSSoft.STORMNET;

    /// <summary>
    /// Сервис данных для работы с объектами ORM для Gis в PostgreSQL + PostGIS.
    /// </summary>
    public class GisPostgresDataService : PostgresDataService
    {

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
                var propName = PutIdentifierIntoBrackets(prop.Name);
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
        /// конвертация значений в строки запроса
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public override string ConvertValueToQueryValueString(object value)
        {
            if (value != null && value.GetType().IsSubclassOf(typeof(Geography)))
            {
                Geography geo = value as Geography;
                return $"ST_GeomFromEWKT('{geo.GetEWKT()}')";
            }
            if (value != null && value.GetType().IsSubclassOf(typeof(Geometry)))
            {
                Geometry geo = value as Geometry;
                return $"ST_GeomFromEWKT('{geo.GetEWKT()}')";
            }
            return base.ConvertValueToQueryValueString(value);
        }

        /// <summary>
        /// Преобразовать значение в SQL строку
        /// </summary>
        /// <param name="sqlLangDef">Определение языка ограничений</param>
        /// <param name="value">Функция</param>
        /// <param name="convertValue">делегат для преобразования констант</param>
        /// <param name="convertIdentifier">делегат для преобразования идентификаторов</param>
        /// <returns></returns>
        public override string FunctionToSql(
            SQLWhereLanguageDef sqlLangDef,
            Function value,
            delegateConvertValueToQueryValueString convertValue,
            delegatePutIdentifierToBrackets convertIdentifier)
        {
            ExternalLangDef langDef = sqlLangDef as ExternalLangDef;
            if (value.FunctionDef.StringedView == "GeoIntersects")
            {
                VariableDef varDef = null;
                Geography geo = null;
                if (value.Parameters[0] is VariableDef && value.Parameters[1] is Geography)
                {
                    varDef = value.Parameters[0] as VariableDef;
                    geo = value.Parameters[1] as Geography;
                }
                else if (value.Parameters[1] is VariableDef && value.Parameters[0] is Geography)
                {
                    varDef = value.Parameters[1] as VariableDef;
                    geo = value.Parameters[0] as Geography;
                }
                if (varDef != null && geo != null)
                {
                    return $"ST_Intersects({varDef.StringedView},ST_GeomFromEWKT('{geo.GetEWKT()}'))";
                }
                if (value.Parameters[0] is VariableDef && value.Parameters[1] is VariableDef)
                {
                    varDef = value.Parameters[0] as VariableDef;
                    VariableDef varDef2 = value.Parameters[1] as VariableDef;
                    return $"ST_Intersects({varDef.StringedView},{varDef2.StringedView})";
                }
                geo = value.Parameters[0] as Geography;
                var geo2 = value.Parameters[0] as Geography;
                return $"ST_Intersects(ST_GeomFromEWKT('{geo.GetEWKT()}'),ST_GeomFromEWKT('{geo2.GetEWKT()}'))";
            }

            if (value.FunctionDef.StringedView == "GeomIntersects")
            {
                VariableDef varDef = null;
                Geometry geo = null;
                if (value.Parameters[0] is VariableDef && value.Parameters[1] is Geometry)
                {
                    varDef = value.Parameters[0] as VariableDef;
                    geo = value.Parameters[1] as Geometry;
                }
                else if (value.Parameters[1] is VariableDef && value.Parameters[0] is Geometry)
                {
                    varDef = value.Parameters[1] as VariableDef;
                    geo = value.Parameters[0] as Geometry;
                }
                if (varDef != null && geo != null)
                {
                    return $"ST_Intersects({varDef.StringedView},ST_GeomFromEWKT('{geo.GetEWKT()}'))";
                }
                if (value.Parameters[0] is VariableDef && value.Parameters[1] is VariableDef)
                {
                    varDef = value.Parameters[0] as VariableDef;
                    VariableDef varDef2 = value.Parameters[1] as VariableDef;
                    return $"ST_Intersects({varDef.StringedView},{varDef2.StringedView})";
                }
                geo = value.Parameters[0] as Geometry;
                var geo2 = value.Parameters[0] as Geometry;
                return $"ST_Intersects(ST_GeomFromEWKT('{geo.GetEWKT()}'),ST_GeomFromEWKT('{geo2.GetEWKT()}'))";
            }

            return base.FunctionToSql(sqlLangDef, value, convertValue, convertIdentifier);
        }
    }
}
