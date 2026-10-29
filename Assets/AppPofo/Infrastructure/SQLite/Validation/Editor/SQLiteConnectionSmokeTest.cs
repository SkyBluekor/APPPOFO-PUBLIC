using UnityEngine;
using System;
using SQLite;
using System.IO;
using UnityApplication = UnityEngine.Application;

namespace AppPofo.Infrastructure.SQLite.Validation
{
    public static class SQLiteConnectionSmokeTest
    {
        private const string DatabaseFileName = "app_pofo_smoke_test.db";

        [UnityEditor.MenuItem("AppPofo/Validation/SQLite/Connection")]
        public static void RunTest()
        {
            string directory = Path.GetFullPath(Path.Combine(UnityApplication.dataPath, "../.utmp/sqlite-validation"));
            Directory.CreateDirectory(directory);
            string databasePath = Path.Combine(directory, DatabaseFileName);

            try
            {
                // using 블록이 끝나면 SQLiteConnection.Dispose()가 호출되어 DB 연결과 네이티브 자원이 자동으로 정리
                using(var connection = new SQLiteConnection(databasePath))
                {
                    // SQLite는 외래키 검사가 기본적으로 꺼져있을수 있으니 연결을 열때마다 반드시 활성화
                    connection.Execute("PRAGMA foreign_keys = ON;");

                    int foreignKeysEnabled = connection.ExecuteScalar<int>("PRAGMA foreign_keys;");

                    if(foreignKeysEnabled != 1)
                    {
                        throw new Exception("SQLite 외래키 설정이 활성화되지 않았습니다.");
                    }                    
                }

                if(!File.Exists(databasePath))
                {
                    throw new Exception($"SQLite 데이터베이스 파일이 생성되지 않았습니다. 경로: {databasePath}");
                }

                Debug.Log("SQLite 연결 테스트 성공: 데이터베이스 파일이 생성되고 외래키 설정이 활성화되었습니다.");
            }
            catch(Exception ex)
            {
                Debug.LogError($"SQLite 연결 테스트 실패: {ex.Message}");
                throw;
            }
        }
    }
}
