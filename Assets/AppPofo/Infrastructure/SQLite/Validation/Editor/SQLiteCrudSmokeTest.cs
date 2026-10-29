using System;
using System.IO;
using SQLite;
using UnityEngine;
using UnityApplication = UnityEngine.Application;

namespace AppPofo.Infrastructure
{
    [Table("app_pofo_crud_validation")]
    public sealed class SQLiteCrudValidationRow
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public string DisplayName { get; set; }

        public int IsActive { get; set; }
    }

    public static class SQLiteCrudSmokeTest
    {
        private const string DatabaseFileName = "app_pofo_crud_test.db";

        [UnityEditor.MenuItem("AppPofo/Validation/SQLite/CRUD")]
        public static void RunTest()
        {
            string directory = Path.GetFullPath(Path.Combine(UnityApplication.dataPath, "../.utmp/sqlite-validation"));
            Directory.CreateDirectory(directory);
            string databasePath = Path.Combine(directory, DatabaseFileName);

            try
            {
                using (var connection = new SQLiteConnection(databasePath))
                {
                    connection.Execute("PRAGMA foreign_keys = ON;");

                    
                    connection.CreateTable<SQLiteCrudValidationRow>();
                    connection.Execute("DELETE FROM app_pofo_crud_validation;"); // 기존 데이터 삭제
                    
                    // 테이블 생성
                    var newRow = new SQLiteCrudValidationRow
                    {
                        DisplayName = "저장 전 이름",
                        IsActive = 1
                    };

                    int insertedCount = connection.Insert(newRow);

                    if(insertedCount != 1)
                    {
                        throw new InvalidOperationException($"INSERT 결과가 예상과 다릅니다: {insertedCount}");
                    }

                    if(newRow.Id <= 0)
                    {
                        throw new InvalidOperationException($"INSERT 후 Id가 유효하지 않습니다: {newRow.Id}");
                    }

                    // 데이터 조회
                    SQLiteCrudValidationRow insertedRow = FindRow(connection, newRow.Id);

                    if(insertedRow.DisplayName != "저장 전 이름" || insertedRow.IsActive != 1)
                    {
                        throw new InvalidOperationException("INSERT 후 조회된 데이터가 예상과 다릅니다.");
                    }

                    // 데이터 업데이트
                    insertedRow.DisplayName = "수정 후 이름";
                    insertedRow.IsActive = 0;
                    int updatedCount = connection.Update(insertedRow);

                    if(updatedCount != 1)
                    {
                        throw new InvalidOperationException($"UPDATE 결과가 예상과 다릅니다: {updatedCount}");
                    }

                    SQLiteCrudValidationRow updatedRow = FindRow(connection, newRow.Id);
                    if(updatedRow.DisplayName != "수정 후 이름" || updatedRow.IsActive != 0)
                    {
                        throw new InvalidOperationException("UPDATE 후 조회된 데이터가 예상과 다릅니다.");
                    }
                    
                    // 데이터 삭제
                    int deletedCount = connection.Delete(updatedRow);

                    if(deletedCount != 1)
                    {
                        throw new InvalidOperationException($"DELETE 결과가 예상과 다릅니다: {deletedCount}");
                    }

                    int remainingCount = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM app_pofo_crud_validation WHERE Id = ?;", newRow.Id);
                    if(remainingCount != 0)
                    {
                        throw new InvalidOperationException("DELETE 후 조회된 데이터가 예상과 다릅니다.");
                    }                    
                }

                Debug.Log("SQLite CRUD Smoke Test 성공: 모든 CRUD 작업이 정상적으로 수행되었습니다.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"SQLite CRUD Smoke Test 실패: {ex.Message}");
                throw;
            }
        }

        private static SQLiteCrudValidationRow FindRow(
            SQLiteConnection connection,
            int id)
        {
            /*
             * SQL 안에 ID를 문자열로 직접 합치지 않고 ? 매개변수를 사용합니다.
             * 값은 SQLite가 별도로 바인딩하므로 SQL과 입력값이 분리됩니다.
             */
            var rows = connection.Query<SQLiteCrudValidationRow>(
                @"SELECT Id, DisplayName, IsActive
                  FROM app_pofo_crud_validation
                  WHERE Id = ?;",
                id);

            if (rows.Count != 1)
            {
                throw new InvalidOperationException(
                    $"ID {id} 조회 결과가 1개가 아닙니다: {rows.Count}");
            }

            return rows[0];
        }
    }
}
