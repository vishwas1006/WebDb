using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Diagnostics;
using WebDB.Models;

namespace WebDB.Controllers
{
    public class HomeController : Controller
    {
        // ================= INDEX =================



        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Index(DbConnectionModel model)
        {
            string conString = $"Host={model.Host};Database={model.Database};Username={model.User};Password={model.Password}";

            try
            {
                using var con = new NpgsqlConnection(conString);
                con.Open();

                HttpContext.Session.SetString("conn", conString);
                TempData["Success"] = "Connected Successfully";

                return RedirectToAction("Dashboard");
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
                return View();
            }
        }

        // ================= DASHBOARD (GET) =================

        [HttpGet]
        public IActionResult Dashboard()
        {
            var conStr = HttpContext.Session.GetString("conn");

            List<string> schemas = new List<string>();

            using var con = new NpgsqlConnection(conStr);
            con.Open();

            var cmd = new NpgsqlCommand(
                "SELECT schema_name FROM information_schema.schemata", con);

            var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                schemas.Add(reader.GetString(0));
            }

            reader.Close();

            ViewBag.Schemas = schemas;

            return View();
        }

        // ================= DASHBOARD (POST) =================

        [HttpPost]
        public IActionResult Dashboard(string schema, string table, string operation)
        {
            var conStr = HttpContext.Session.GetString("conn");

            List<string> schemas = new List<string>();
            List<string> tables = new List<string>();
            List<Dictionary<string, object>> rows = new List<Dictionary<string, object>>();
            List<string> columns = new List<string>();

            using var con = new NpgsqlConnection(conStr);
            con.Open();

            // ===== Load Schemas =====
            var schemaCmd = new NpgsqlCommand(
                "SELECT schema_name FROM information_schema.schemata", con);

            var schemaReader = schemaCmd.ExecuteReader();

            while (schemaReader.Read())
            {
                schemas.Add(schemaReader.GetString(0));
            }
            schemaReader.Close();

            // ===== Load Tables =====
            if (!string.IsNullOrWhiteSpace(schema))
            {
                var tableCmd = new NpgsqlCommand(
                    $"SELECT table_name FROM information_schema.tables WHERE table_schema = '{schema}'",
                    con);

                var tableReader = tableCmd.ExecuteReader();

                while (tableReader.Read())
                {
                    tables.Add(tableReader.GetString(0));
                }
                tableReader.Close();
            }

            // ===== SELECT =====
            if (operation == "select" && !string.IsNullOrWhiteSpace(table))
            {
                var dataCmd = new NpgsqlCommand($"SELECT * FROM {schema}.{table}", con);
                var dataReader = dataCmd.ExecuteReader();

                while (dataReader.Read())
                {
                    var row = new Dictionary<string, object>();

                    for (int i = 0; i < dataReader.FieldCount; i++)
                    {
                        row[dataReader.GetName(i)] = dataReader[i];
                    }

                    rows.Add(row);
                }
                dataReader.Close();
            }

            // ===== INSERT (LOAD COLUMNS) =====
            else if (operation == "insert" && !string.IsNullOrWhiteSpace(table))
            {
                var colCmd = new NpgsqlCommand(
                    $"SELECT column_name FROM information_schema.columns WHERE table_schema='{schema}' AND table_name='{table}'",
                    con);

                var colReader = colCmd.ExecuteReader();

                while (colReader.Read())
                {
                    columns.Add(colReader.GetString(0));
                }
                colReader.Close();
            }

            // ===== INSERT DATA =====
            else if (operation == "insertdata" && Request.Form["operation"] == "insertdata")
            {
                var form = Request.Form;

                columns = form.Keys
                .Where(k => !k.StartsWith("__") && k != "schema" && k != "table" && k != "operation")
                .ToList();

                var values = columns.Select(c => form[c].ToString()).ToList();

                var colNames = string.Join(",", columns);
                var paramNames = string.Join(",", columns.Select(c => "@" + c));

                var insertCmd = new NpgsqlCommand(
                    $"INSERT INTO {schema}.{table} ({colNames}) VALUES ({paramNames})",
                    con);

                for (int i = 0; i < columns.Count; i++)
                {
                    var val = values[i];

                    if (columns[i].ToLower().Contains("id") || columns[i].ToLower().Contains("age"))
                    {
                        if (!int.TryParse(val, out int intVal))
                        {
                            ViewBag.Error = $"Invalid integer for '{columns[i]}'";
                            return View();
                        }

                        insertCmd.Parameters.AddWithValue(columns[i], intVal);
                    }
                    else
                    {
                        insertCmd.Parameters.AddWithValue(columns[i], val);
                    }
                }

                insertCmd.ExecuteNonQuery();

                TempData["Success"] = "Inserted successfully";


                operation = "select"; //setting to select 
            }
           

            //update to load columns

           else if (operation == "update")
            {
                var colCmd = new NpgsqlCommand(
                    $"Select column_name from information_schema.columns where table_schema='{schema}' and table_name='{table}'",
                    con);

                var colReader = colCmd.ExecuteReader();

                while (colReader.Read())
                {
                    columns.Add(colReader.GetString(0));
                }
                colReader.Close();
            }

            //update data
           else if (operation == "updatedata" && !string.IsNullOrWhiteSpace(table))
        {
            var id = Request.Form["id"].ToString();
            var column = Request.Form["column"].ToString();
            var value = Request.Form["value"].ToString();

            if (!int.TryParse(id, out int intId))
            {
                ViewBag.Error = "Invalid Id";
                return View();
            }

            var updateCmd = new NpgsqlCommand(
                $"UPDATE {schema}.{table} SET {column} = @value WHERE id = @id",
                con);

            // handle integer columns
            if (column.ToLower().Contains("id") || column.ToLower().Contains("age"))
            {
                if (!int.TryParse(value, out int intVal))
                {
                    ViewBag.Error = $"Invalid integer for '{column}'";
                    return View();
                }

                updateCmd.Parameters.AddWithValue("value", intVal);
            }
            else
            {
                updateCmd.Parameters.AddWithValue("value", value);
            }

            updateCmd.Parameters.AddWithValue("id", intId);

            updateCmd.ExecuteNonQuery();

            TempData["Success"] = "Updated successfully";

            operation = "select";
        }
            //Delete data
            else if (operation == "deletedata" && !string.IsNullOrWhiteSpace(table))
            {
                var id = Request.Form["id"].ToString();

                if (!int.TryParse(id, out int intId))
                {
                    ViewBag.Error = "Invalid ID";
                    return View();
                }

                var deleteCmd = new NpgsqlCommand(
                $"DELETE FROM {schema}.{table} WHERE id = @id",
                con);

                deleteCmd.Parameters.AddWithValue("id", intId);

                int rowsAffected = deleteCmd.ExecuteNonQuery();

                if (rowsAffected == 0)
                {
                    ViewBag.Error = "No record found with that ID";
                }
                else
                {
                    TempData["Success"] = "Deleted Successfully";
                }
                operation = "select";
            }

            // ===== SEND DATA TO VIEW =====
            ViewBag.Schemas = schemas;
            ViewBag.Tables = tables;
            ViewBag.Rows = rows;
            ViewBag.Columns = columns;
            ViewBag.SelectedSchema = schema;
            ViewBag.SelectedTable = table;
            ViewBag.Operation = operation;

            return View();
        }

        // ================= OTHER =================

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}