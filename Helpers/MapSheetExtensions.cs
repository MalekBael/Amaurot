using SaintCoinach;
using SaintCoinach.Ex;
using SaintCoinach.Ex.Relational;
using SaintCoinach.Xiv;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Amaurot
{
    public static class MapSheetExtensions
    {
        public static int GetColumnIndex(this IXivSheet sheet, string columnName)
        {
            var sheetType = sheet.GetType();

            var properties = sheetType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var prop in properties)
            {
                if (prop.PropertyType.IsAssignableFrom(typeof(IRelationalSheet)) ||
                    prop.PropertyType.Name.Contains("Sheet"))
                {
                    var relSheet = prop.GetValue(sheet) as IRelationalSheet;
                    if (relSheet != null)
                    {
                        var headers = relSheet.Header;
                        if (headers != null)
                        {
                            for (int i = 0; i < headers.ColumnCount; i++)
                            {
                                if (string.Equals(headers.GetColumn(i).Name, columnName, StringComparison.OrdinalIgnoreCase))
                                {
                                    return i;
                                }
                            }
                        }
                    }
                }
            }

            return -1;
        }

        public static IEnumerable<string> ColumnNames<T>(this IXivSheet<T> sheet) where T : IXivRow
        {
            var columnNames = new List<string>();

            var sheetType = sheet.GetType();

            var properties = sheetType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var prop in properties)
            {
                if (prop.PropertyType.IsAssignableFrom(typeof(IRelationalSheet)) ||
                    prop.PropertyType.Name.Contains("Sheet"))
                {
                    var relSheet = prop.GetValue(sheet) as IRelationalSheet;
                    if (relSheet != null)
                    {
                        var headers = relSheet.Header;
                        if (headers != null)
                        {
                            for (int i = 0; i < headers.ColumnCount; i++)
                            {
                                columnNames.Add(headers.GetColumn(i).Name);
                            }
                        }
                    }
                }
            }

            return columnNames;
        }

        public static IEnumerable<string> GetColumnNamesFromSample<T>(this IXivSheet<T> sheet) where T : IXivRow
        {
            var columnNames = new HashSet<string>();

            var firstRow = sheet.FirstOrDefault();
            if (firstRow != null)
            {
                var rowType = firstRow.GetType();
                foreach (var prop in rowType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    columnNames.Add(prop.Name);
                }
            }

            return columnNames;
        }
    }
}