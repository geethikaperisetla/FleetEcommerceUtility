using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml;
using System.Data.SqlClient;
using System.Configuration;
using System.Data;
using System.ServiceProcess;
using System.Threading;

namespace FleetEcommerceUtility
{

    class Program
    {
        
        static void Main()
        {

            string conn = ConfigurationManager.ConnectionStrings["ProdConn"].ConnectionString;
            string sqlQuery = "select A.RCPT_NO,A.SRCE_PGM_ID from DHTP970_DOC_RCPT A where RCPT_NO=17061279";//16843296";
            //string sqlQuery = "select A.RCPT_NO,A.SRCE_PGM_ID from DHTP970_DOC_RCPT A where not exists(select 1 from DHTP820_DOC_DATA B where A.RCPT_NO = B.RCPT_NO and B.REQ_TYPE_CD = 'CONFIRM') and SRCE_PGM_ID like 'DY%' and year(creat_ts) = " + DateTime.Today.Year + " and month(creat_ts) = " + DateTime.Today.Month;
            
            DataTable dt = ExecuteSelectQuery(conn, sqlQuery);
            
            string xmlContent = "";
            string modifiedXmlContent = string.Empty;
            foreach (DataRow dr in dt.Rows)
            {
                string rcptNo = dr["RCPT_NO"].ToString();
                xmlContent = Readfrom970(conn, rcptNo);

                if(xmlContent.Contains("'"))
                {
                    xmlContent = xmlContent.Replace("'", "\"");
                }

                if (xmlContent.Contains("</cXML></cxml>"))
                {
                    xmlContent = xmlContent.Replace("</cXML></cxml>", "</cXML>");
                }

                modifiedXmlContent = CapitalizeElementNames(xmlContent, rcptNo);
                modifiedXmlContent = FormatXMLTag(modifiedXmlContent, "<ConfirmationItem");

                
                if (modifiedXmlContent==string.Empty)
                {
                    xmlContent = CorrectXMLTag(xmlContent, "ConfirmationRequest>");
                    xmlContent = CorrectXMLTag(xmlContent, "ConfirmationItem");
                    modifiedXmlContent = CapitalizeElementNames(xmlContent, rcptNo);
                }

                if(xmlContent.Contains("</orderreference></confirmationrequest></request></cxml>") && modifiedXmlContent == string.Empty)
                {
                    xmlContent = CorrectXMLTag(xmlContent, "/orderreference></confirmationrequest></request></cxml>");
                    modifiedXmlContent = CapitalizeElementNames(xmlContent, rcptNo);
                }

                if (modifiedXmlContent.Trim() != string.Empty)
                {
                    if(!modifiedXmlContent.Contains("</DocumentReference>"))
                    modifiedXmlContent = ChangeXMLTag(modifiedXmlContent, "DocumentReference");
                    modifiedXmlContent = ChangeXMLTag(modifiedXmlContent, "Extrinsic");
                    //modifiedXmlContent = modifiedXmlContent.Replace("/>", "></DocumentReference>");
                    if (modifiedXmlContent != "")
                    {
                        Insert970(conn, modifiedXmlContent, rcptNo,Convert.ToString(dr["SRCE_PGM_ID"]));

                        Console.WriteLine("Processed Receipts" + rcptNo);
                    }
                    else
                    {
                        string lastWord = GetLastWordFromXml(xmlContent);
                        if (lastWord != "</cxml>")
                        {
                            try
                            {
                                modifiedXmlContent = xmlContent.Replace("</cxml>", "");
                                modifiedXmlContent = modifiedXmlContent.Trim() + "</cxml>";
                                Insert970(conn, modifiedXmlContent, rcptNo, Convert.ToString(dr["SRCE_PGM_ID"]));
                                Console.WriteLine("Updated Receipts" + rcptNo);
                                
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("Errored Receipts " + rcptNo);
                            }

                        }

                    }
                }
               
            }
          
           // Console.ReadLine();
        }

        static string CapitalizeElementNames(string xmlContent,string rcptNo)
        {
            try
            {
                XmlDocument originalDoc = new XmlDocument();
                originalDoc.LoadXml(xmlContent);

                XmlNode newRoot = CapitalizeElement(originalDoc.DocumentElement);

                XmlDocument newDoc = new XmlDocument();
                newDoc.AppendChild(newDoc.ImportNode(newRoot, true));

                return newDoc.OuterXml;
            }
            catch (Exception ex)
            {
                //WritetoLog.WriteLog _writelog = new WritetoLog.WriteLog();
                //_writelog.LogToFile("Errored Receipts : " + rcptNo, AppDomain.CurrentDomain.BaseDirectory);
               // Console.WriteLine(ex.Message);
                return "";
            }
           
        }

        static XmlNode CapitalizeElement(XmlNode oldNode)
        {
            XmlNode newNode;

            if (oldNode.NodeType == XmlNodeType.Element)
            {
                XmlElement oldElement = (XmlElement)oldNode;

                string newElementName = char.ToUpper(oldElement.LocalName[0]) + oldElement.LocalName.Substring(1);
                Dictionary<string, string> replacements = new Dictionary<string, string>
                    {
                         { "CXML", "cXML" },
                        { "Cxml", "cXML" },
                        { "payloadid", "payloadID" },
                        { "orderid","orderID"},
                        { "noticedate","noticeDate"},
                        { "orderdate","orderDate"},
                        {"Useragent","UserAgent" },
                        {"Confirmationrequest","ConfirmationRequest" },
                        {"Confirmationheader","ConfirmationHeader" },
                        {"Orderreference","OrderReference" },
                        {"Documentreference","DocumentReference" },
                        {"documentreference","DocumentReference" }
                    };
                newNode = oldNode.OwnerDocument.CreateElement(ReplaceTokens(newElementName,replacements));

                // Copy attributes
                foreach (XmlAttribute attribute in oldElement.Attributes)
                {
                    ((XmlElement)newNode).SetAttribute(ReplaceTokens(attribute.Name,replacements), ReplaceTokens(attribute.Value,replacements));
                }

                // Copy child nodes
                foreach (XmlNode childNode in oldElement.ChildNodes)
                {
                    XmlNode newChildNode = CapitalizeElement(childNode);
                    newNode.AppendChild(newChildNode);
                }
            }
            else
            {
                newNode = oldNode.CloneNode(true);
            }

            return newNode;
        }

        // Function to perform find-and-replace using a dictionary
        static string ReplaceTokens(string input, Dictionary<string, string> replacements)
        {
            foreach (var replacement in replacements)
            {
                input = input.Replace(replacement.Key, replacement.Value);
            }
            return input;
       }

        static string Readfrom970(string connectionString, string data)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    // Use the value from the file to query the first table
                    using (SqlCommand command = new SqlCommand("SELECT PROC_DOC_DATA FROM DHTP970_DOC_RCPT WHERE RCPT_NO = @data", connection))
                    {
                        command.Parameters.AddWithValue("@data", data);
                        object result = command.ExecuteScalar();
                        return result != null ? result.ToString() : null;
                    }
                }
            }

            catch (Exception ex)
            {
                Console.WriteLine($"Error querying database: {ex.Message}");
                return null;
            }
        }

        static void Insert970(string connectionString, string data,string rcptno,string SRCE_PGM_ID)

        {
            try

            {

                using (SqlConnection connection = new SqlConnection(connectionString))
                {

                    connection.Open();

                    using (SqlCommand insertCommand = new SqlCommand("INSERT INTO DHTP970_DOC_RCPT (PROC_DOC_DATA, SRCE_PGM_ID, CREAT_TS) VALUES (@data, @SRCE_PGM_ID, GETDATE());", connection))
                    {
                        insertCommand.Parameters.AddWithValue("@data", data); // Assuming 'data' is the value you want to insert
                        insertCommand.Parameters.AddWithValue("@SRCE_PGM_ID", SRCE_PGM_ID); // Assuming 'SRCE_PGM_ID' is the value you want to insert

                        int rowsAffected = insertCommand.ExecuteNonQuery();
                        if (rowsAffected == 2)
                        {
                          //  WritetoLog.WriteLog writeDeletelog = new WritetoLog.WriteLog();
                            using (SqlCommand command = new SqlCommand("Delete From DHTP971_RCPT_QUEUE where RCPT_NO=@rcptno", connection))
                            {
                                
                               // writeDeletelog.LogToFile("Delete Record from 971 : " + rcptno, AppDomain.CurrentDomain.BaseDirectory);

                                command.Parameters.AddWithValue("@rcptno", rcptno);

                                 command.ExecuteNonQuery();

                            }

                            using (SqlCommand command = new SqlCommand("Delete From DHTP970_DOC_RCPT where RCPT_NO=@rcptno", connection))
                            {
                                
                                //writeDeletelog.LogToFile("Delete Record from 970 : " + rcptno  + "-"+ SRCE_PGM_ID+"\n" + data , AppDomain.CurrentDomain.BaseDirectory);

                                command.Parameters.AddWithValue("@rcptno", rcptno);

                                 command.ExecuteNonQuery();


                            }

                        }
                        else
                        {
                            // Insert failed
                        }
                    }

                 

                   
                    

                }

            }

            catch (Exception ex)

            {

                Console.WriteLine($"Error updating database: {ex.Message}");

            }

        }

      
        static DataTable ExecuteSelectQuery(string connectionString, string selectQuery)
        {
            DataTable dataTable = new DataTable();

            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                using (SqlCommand command = new SqlCommand(selectQuery, connection))
                using (SqlDataAdapter adapter = new SqlDataAdapter(command))
                {
                    connection.Open();
                    adapter.Fill(dataTable);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                // Handle the exception according to your needs
            }

            return dataTable;
        }

        static string GetLastWordFromXml(string xmlString)
        {
            xmlString = xmlString.Trim();
            // Find the last closing tag in the XML
            int lastClosingTagIndex = xmlString.LastIndexOf('>');

            // Extract the content after the last closing tag
            string lastElementContent = xmlString.Substring(lastClosingTagIndex + 1);

            // Find the last opening tag in the extracted content
            int lastOpeningTagIndex = xmlString.LastIndexOf('<');

            // Extract the text content between the opening and closing tags
            string textContent = xmlString.Substring(lastOpeningTagIndex + 1, xmlString.Length - lastOpeningTagIndex - 2);

            // Split the text content into words and get the last word
            string[] words = textContent.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            string lastWord = words.Length > 0 ? words[words.Length - 1] : "";

            return lastWord;
        }

        static string ChangeXMLTag(string xmlString, string tagName)
        {
            //string Entrinsictemp = string.Empty;
            
                string temp = string.Empty;
                string finalXML = string.Empty;
                string[] ModifiedXML = new string[] { "<" + tagName };
                string strsplittext = Convert.ToString(xmlString);
                string[] str = strsplittext.Split(ModifiedXML, StringSplitOptions.None);
                if (tagName == "Extrinsic")
                {
                    for (int i = 0; i < str.Length; i++)
                    {
                        if (str[i].Contains("/>"))
                        {
                            temp = temp + "<" + tagName + str[i].ToString().Remove(str[i].ToString().IndexOf("/>"), 2).Insert(str[i].ToString().IndexOf("/>"), "> </" + tagName + ">");
                        }
                        else
                        {
                            if (str[i].Contains("</Extrinsic>"))
                            {
                                temp += "<" + tagName;
                            }
                            temp += str[i];
                        }
                        //temp += str[i];
                    }

                }
                else
                {
                    //if (!str[1].ToString().IndexOf("/>") != -1)
                    //{
                        temp = str[0] + "<" + tagName + str[1].ToString().Remove(str[1].ToString().IndexOf("/>"), 2).Insert(str[1].ToString().IndexOf("/>"), "> </" + tagName + ">");
                    //}
                    //else
                    //{
                    //    temp = str[0] + "<" + tagName + str[1];
                    //}
                }
                 finalXML = temp;
                return finalXML;
        }

        static string FormatXMLTag(string xmlString, string tagName)
        {
            string temp = string.Empty;
            string finalXML = string.Empty;
            string[] ModifiedXML = new string[] { tagName };
            string strsplittext = Convert.ToString(xmlString);
            string[] str = strsplittext.Split(ModifiedXML, StringSplitOptions.None);
            if (str[0].Contains("</Request>"))
                {
                str[0] = str[0].Replace("</ConfirmationRequest>", string.Empty);
                str[0] = str[0].Replace("</Request>", string.Empty);
                temp = str[0];
            }

            for(int i=1;i<str.Length;i++)
            {
                str[i]= "<ConfirmationItem " + str[i];
                if(i==str.Length-1)
                {
                    str[i] = str[i].Replace("</cXML>", "</ConfirmationRequest> </Request> </cXML>");
                }
                temp = temp + str[i];
            }
            return finalXML = temp;
        }

        static string CorrectXMLTag(string xmlString, string tagName)
        {
            string temp = string.Empty;
            string finalXML = string.Empty;
            string[] ModifiedXML = new string[] { "<" + tagName };
            string strsplittext = Convert.ToString(xmlString);
            string[] str = strsplittext.Split(ModifiedXML, StringSplitOptions.None);
           
            for (int i = 0; i < str.Length; i++)
            {
                if (i > 0 )
                {
                    if (str[i].Contains("</DocumentReference>") && !str[i].Contains("<DocumentReference "))
                    {
                        str[i] = "<" + tagName + str[i].ToString().Replace("</DocumentReference>", "</Extrinsic>");
                    }
                    if ((!str[i].Contains("<" + tagName)) && (str[i].Contains("</ConfirmationItem>")))
                    {
                        str[i] = "<" + tagName + str[i];
                    }


                    if (ModifiedXML[0] == "<ConfirmationRequest>" && !str[i].Contains("</ConfirmationRequest>"))
                    {
                        str[i] = "<" + tagName + str[i].Replace("</cXML>", "</ConfirmationRequest></cXML>");
                        
                    }
                    if (str[0].Contains("<Request>") && str[str.Length-1] != "</Request>")
                    {
                        str[i] = str[i].Replace("</cXML>", "</Request></cXML>");
                    }
                }
                if (tagName == "/orderreference></confirmationrequest></request></cxml>")
                {
                    str[0] += "</orderreference></confirmationrequest></request>";
                    str[1] += "</cxml>";
                    return str[0] + str[1];
                }
                
                    temp += str[i];
                
            }
            return finalXML = temp;
        }

    }

}
// echoooo

