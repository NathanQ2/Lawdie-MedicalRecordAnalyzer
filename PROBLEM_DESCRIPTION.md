# Medical Record Analysis & Demand Drafting
A Personal Injury (PI) case comes in with a stack of medical records — treatment notes, provider records, sometimes imaging reports. A junior associate currently reads all of it and writes a demand letter narrative section: injuries, treatment timeline, providers, diagnoses, causally connected to the incident. That's the task you're automating: records in, structured facts out, demand-style narrative out.

# Why This is Harder Than it Looks
- Document quality varies wildly. Real intake includes clean digital records, but also faxes, handwritten provider notes, and scans where OCR quality is genuinely bad.
- Volume degrades context. A single case can run to thousands of pages across providers. Passing everything into one long-context call is often not viable — for cost, latency, and accuracy. We're specifically interested in how you scope what the model sees per fact rather than dumping everything into one context window.
- Hallucination is HUGE here. A demand narrative gets sent to insurance adjusters. An invented injury, a wrong date, or a fabricated causal claim is a real liability problem for a law firm. We want to see how you constrain generation to what's actually supported by the source documents — grounding claims to specific pages/passages.
- Facts conflict. Different providers sometimes give contradictory info (dates, diagnoses). We want to see how your system surfaces that instead of picking one silently.

# The Pipeline
1. Ingest medical recored documents in a variety of formats (PDFs, scans, image files, etc)
2. Extract structured facts (injuries, treatment dates, providers, diagnoses, and anything else a demand letter needs)
3. Drafts a demand-letter-style narrative section from those structured facts, with each claim traceable back to source documents. The demand letter needs to tie the client information into a narrative of pain and suffering.

# The Program
## Frontend
For now, we will write a CLI interface that allows users to step through the pipeline. The usage should look like: 
- ```MedicalRecordAnalyzer ingest [DIRECTORY_OF_SOURCE_FILES]```: ingest the source material, extract structured facts, export that data into a file
- ```MedicalRecordAnalyzer draft [PATH_TO_EXPORTED_FILE]```: drafts a demand letter using the provided context
## Backend
The backend is intented to run in a cloud environment, so choose technologies that make sense in this context.
### The Ingest Process
1. The program will read over each file in the provided directory. For any files that need OCR to be run, it will run OCR.
2. For each source file, extract all relevant information to the case (injuries, treatment dates, providers, diagnoses, and anything else a demand letter needs). It must include specific quotes + citiations for each peice of information.
3. A case summary will be generated
4. Using all the gathered data, the program will review the case summary and quote to verify that the LLM did not halucinate. The program will also save any contradictory information that can be highlighted to the the user. Note, a file can contradict with itself.
5. For readability, the exported file will be in JSON, but in the future a binary format should be supported. 

Json File Example:
```json 
{
  "summary": "[CASE_SUMMARY]",
  "sources": [
    {
      "path": "[PATH_TO_SOURCE_FILE]",
      "summary": "[SOURCE_SUMMARY]",
      "quotes": [
        {
          "quote": "[QUOTE_TEXT]",
          "location": "[LOCATION_TO_QUOTE_IN_SOURCE_FILE]"
        }
      ]
    }
  ],
  "contradictions": [
    {
      "paths": ["[PATH_TO_SOUCE_1]", "[PATH_TO_SOURCE_2]", "[PATH_TO_SOURCE_N]"]
      "summaries": [ // Summaries of each contradiction
        {
          "contradiction": "[SUMMARY_OF_CONTRADICTION]",
          "location": "[LOCATION_OF_CONTRADICTION_IN_SOURCE]"
        }
      ]
    }
  ]
}
```
### The Draft Process
1. The program will read the json file provided by the user
2. A draft demand letter will be written based off the information in the file. If necessary the LLM can read individual source files. 
3. The draft can be exported as markdown of plain text.
### LLM Provider
- Must have a LLMProvider layer to make LLM queries provider agnostic.
- Required: Support for OpenAI or Anthropic models
