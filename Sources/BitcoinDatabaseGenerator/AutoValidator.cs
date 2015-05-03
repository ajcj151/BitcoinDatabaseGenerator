﻿//-----------------------------------------------------------------------
// <copyright file="AutoValidator.cs">
// Copyright © Ladislau Molnar. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace BitcoinDatabaseGenerator
{
    using System;
    using System.Data;
    using System.Data.SqlClient;
    using System.Globalization;
    using System.IO;
    using System.Threading.Tasks;
    using BitcoinDataLayerAdoNet;
    using ResharperAnnotations;

    public class AutoValidator
    {
        private const int ValidationSqlCommandTimeout = 1200;

        private readonly DatabaseConnection databaseConnection;

        public AutoValidator(string validationDatabaseName)
        {
            this.databaseConnection = DatabaseConnection.CreateSqlServerConnection("localhost", validationDatabaseName);
        }

        public async Task<bool> Validate()
        {
            Console.WriteLine();

            await this.PrepareDumpFolder();
            return this.ValidateDataAgainstBaseline();
        }

        private static bool CompareFiles(string pathToFile1, string pathToFile2)
        {
            string contentOfFirstFile = File.ReadAllText(pathToFile1);
            string[] linesInFirstFile = contentOfFirstFile.Split(new char[] { '\n' });

            string contentOfSecondFile = File.ReadAllText(pathToFile2);
            string[] linesInSecondFile = contentOfSecondFile.Split(new char[] { '\n' });

            if (linesInFirstFile.Length != linesInSecondFile.Length)
            {
                Console.Error.WriteLine("The dataset files have different sizes. See files:\n{0}\n{1}", pathToFile1, pathToFile2);
                return false;
            }

            for (int lineIndex = 0; lineIndex < linesInFirstFile.Length; lineIndex++)
            {
                if (string.CompareOrdinal(linesInFirstFile[lineIndex], linesInSecondFile[lineIndex]) != 0)
                {
                    Console.Error.WriteLine("The dataset files have different content in line {0}. See files:\n{1}\n{2}", lineIndex, pathToFile1, pathToFile2);
                    return false;
                }
            }

            Console.WriteLine("Dataset verified.");
            return true;
        }

        private static string GetPathToDumpFolder()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}{1}", System.IO.Path.GetTempPath(), "BitcoinDatabaseGenerator");
        }

        private static void DumpResultsToFile<T>(StreamWriter dumpFile, string validationDatasetFileName, ValidationDataSetInfo<T> validationDataSetInfo) where T : DataSet, new()
        {
            dumpFile.WriteLine("Validation dataset: {0}\r\n", validationDatasetFileName);

            DumpResultsHeaderToFile<T>(dumpFile, validationDataSetInfo);
            
            dumpFile.WriteLine();
            
            DumpDataTableToFile(dumpFile, validationDataSetInfo.DataSet.Tables[0]);
        }

        private static void DumpResultsHeaderToFile<T>(StreamWriter dumpFile, ValidationDataSetInfo<T> validationDataSetInfo) where T : DataSet, new()
        {
            dumpFile.WriteLine("SQL statement:\r\n{0}\r\n", validationDataSetInfo.SqlStatement);

            foreach (SqlParameter sqlParameter in validationDataSetInfo.SqlParameters)
            {
                dumpFile.WriteLine("Parameter {0}. Value: {1}.", sqlParameter.ParameterName, sqlParameter.Value);
            }

            dumpFile.WriteLine();

            dumpFile.WriteLine("Columns:");
            foreach (DataColumn column in validationDataSetInfo.DataSet.Tables[0].Columns)
            {
                dumpFile.WriteLine(column.ColumnName);
            }
        }

        private static void DumpDataTableToFile(StreamWriter dumpFile, DataTable dataTable)
        {
            for (int r = 0; r < dataTable.Rows.Count; r++)
            {
                DataRow row = dataTable.Rows[r];
                dumpFile.WriteLine("Row {0}", r);

                for (int c = 0; c < dataTable.Columns.Count; c++)
                {
                    if (row[c] is DBNull)
                    {
                        dumpFile.Write("<null>");
                    }
                    else
                    {
                        dumpFile.Write(DbValueTostring(row[c]));
                    }

                    dumpFile.WriteLine();
                }

                dumpFile.WriteLine();
            }
        }

        private static string DbValueTostring(object value)
        {
            byte[] byteArray = value as byte[];
            if (byteArray != null)
            {
                return BitConverter.ToString(byteArray);
            }

            if (value is DateTime)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0} [Ticks: {1}]", value.ToString(), ((DateTime)value).Ticks.ToString(CultureInfo.InvariantCulture));
            }

            return value.ToString();
        }

        private static string CopyBaselineFile(string validationDatasetFileName)
        {
            string pathToOriginalBaselineFile = string.Format(
                CultureInfo.InvariantCulture,
                "{0}\\ValidationBaseline\\{1}.txt",
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                validationDatasetFileName);

            string pathToDumpBaselineFile = string.Format(CultureInfo.InvariantCulture, "{0}\\{1}_Baseline.txt", GetPathToDumpFolder(), validationDatasetFileName);

            File.Copy(pathToOriginalBaselineFile, pathToDumpBaselineFile);

            return pathToDumpBaselineFile;
        }

        private static string GetPathToDumpFile(string validationDatasetFileName)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}\\{1}_Actual.txt", GetPathToDumpFolder(), validationDatasetFileName);
        }

        private static bool ValidateDataSet<T>(string validationDatasetFileName, [InstantHandle] Func<ValidationDataSetInfo<T>> retrieveValidationDatasetInfo) where T : DataSet, new()
        {
            Console.WriteLine();
            Console.WriteLine("Validating dataset: {0}. Please wait...", validationDatasetFileName);

            string pathToBaselineFile = CopyBaselineFile(validationDatasetFileName);

            ValidationDataSetInfo<T> validationDataSetInfo = retrieveValidationDatasetInfo();

            if (validationDataSetInfo.DataSet.Tables[0].Rows.Count == 1)
            {
                Console.WriteLine("One row was retrieved.");
            }
            else
            {
                Console.WriteLine("{0} rows were retrieved.", validationDataSetInfo.DataSet.Tables[0].Rows.Count);
            }

            string pathToDumpFile = GetPathToDumpFile(validationDatasetFileName);
            using (StreamWriter dumpFile = new StreamWriter(pathToDumpFile))
            {
                DumpResultsToFile(dumpFile, validationDatasetFileName, validationDataSetInfo);
            }

            return CompareFiles(pathToDumpFile, pathToBaselineFile);
        }

        private bool ValidateDataAgainstBaseline()
        {
            //// These are values we can use to produce validation baselines for a smaller sample.
            //// const int maxBlockFileId = 3;
            //// const int blockSampleRatio = 1000;
            //// const int transactionSampleRatio = 1000;
            //// const int transactionInputSampleRatio = 10000;
            //// const int transactionOutputSampleRatio = 10000;

            const int maxBlockFileId = 250;
            const int blockSampleRatio = 500;
            const int transactionSampleRatio = 100000;
            const int transactionInputSampleRatio = 200000;
            const int transactionOutputSampleRatio = 200000;

            bool validationResult;

            using (BitcoinDataLayer bitcoinDataLayer = new BitcoinDataLayer(this.databaseConnection.ConnectionString, ValidationSqlCommandTimeout))
            {
                validationResult = ValidateDataSet("01_BlockchainData", () => bitcoinDataLayer.GetValidationBlockchainDataSet(maxBlockFileId));
                validationResult = ValidateDataSet("02_BlockFilesData", () => bitcoinDataLayer.GetValidationBlockFilesDataSet(maxBlockFileId)) && validationResult;
                validationResult = ValidateDataSet("03_BlockSampleData", () => bitcoinDataLayer.GetValidationBlockSampleDataSet(maxBlockFileId, blockSampleRatio)) && validationResult;
                validationResult = ValidateDataSet("04_TransactionSampleData", () => bitcoinDataLayer.GetValidationTransactionSampleDataSet(maxBlockFileId, transactionSampleRatio)) && validationResult;
                validationResult = ValidateDataSet("05_TransactionInputSampleData", () => bitcoinDataLayer.GetValidationTransactionInputSampleDataSet(maxBlockFileId, transactionInputSampleRatio)) && validationResult;
                validationResult = ValidateDataSet("06_TransactionOutputSampleData", () => bitcoinDataLayer.GetValidationTransactionOutputSampleDataSet(maxBlockFileId, transactionOutputSampleRatio)) && validationResult;
            }

            return validationResult;
        }

        private async Task PrepareDumpFolder()
        {
            string pathToDumpFolder = GetPathToDumpFolder();
            if (Directory.Exists(pathToDumpFolder))
            {
                Directory.Delete(pathToDumpFolder, true);
            }

            // Wait for the folder to actually be deleted. Otherwise we'll create the new folder before the delete method completes 
            // and when the delete method eventually completes, the new folder will be deleted as well.
            while (Directory.Exists(pathToDumpFolder))
            {
                await Task.Delay(100);
            }

            Directory.CreateDirectory(pathToDumpFolder);

            Console.WriteLine("Path to validation data files:");
            Console.WriteLine(GetPathToDumpFolder());
        }
    }
}
