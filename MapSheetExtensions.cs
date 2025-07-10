using SaintCoinach;
using SaintCoinach.Ex;
using SaintCoinach.Ex.Relational;
using SaintCoinach.Xiv;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace map_editor
{
    public static class MapSheetExtensions
    {
        // Extension method to get column index by name for IXivSheet
        public static int GetColumnIndex(this IXivSheet sheet, string columnName)
        {
            // Try to get the sheet as a RelationalSheet through reflection
            var sheetType = sheet.GetType();
            
            // Try to get a backing field or property that might be a RelationalSheet
            var properties = sheetType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var prop in properties)
            {
                if (prop.PropertyType.IsAssignableFrom(typeof(IRelationalSheet)) ||
                    prop.PropertyType.Name.Contains("Sheet"))
                {
                    var relSheet = prop.GetValue(sheet) as IRelationalSheet;
                    if (relSheet != null)
                    {
                        // Try to find the column index
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
            
            // If we can't find it through reflection, return -1
            return -1;
        }
        
        // Extension method to get column names for IXivSheet<T>
        public static IEnumerable<string> ColumnNames<T>(this IXivSheet<T> sheet) where T : IXivRow
        {
            var columnNames = new List<string>();
            
            // Try to get the sheet as a RelationalSheet through reflection
            var sheetType = sheet.GetType();
            
            // Try to get a backing field or property that might be a RelationalSheet
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
        
        // Alternative implementation that might work if we can't get the column names from header
        public static IEnumerable<string> GetColumnNamesFromSample<T>(this IXivSheet<T> sheet) where T : IXivRow
        {
            var columnNames = new HashSet<string>();
            
            // Try to find properties on the first item
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