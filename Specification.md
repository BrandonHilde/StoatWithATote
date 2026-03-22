## Stoat Tote:

A minimalist vibe code cli. No giant system prompts and tool calls. Just straight code editing. A stoat that brings you code in his tote.

### Complete:

1. Create a vibe coders cli with very basic functionality.

2. Create the necesary code to connect to local ollama hosted models.

3. The cli gets a list of the documents needed for the request.

4. The LLM responds with a list of files it wants to see.

5. The cli packages them into a single request.

6. The LLM replies with the full files in a single response.

7. The cli replaces the files with the new contents after verification and making a back up of those files.

This reduces the number of requests necessary to accomplish a task.


### TODO: 

We need to update the system prompts to give the LLM more access to the machine. I dont want it to use the terminal directly. I want it to make requests and have stoat tote do all the opperations. 

Opperations: 

1. Create a file
2. Rename a file
3. Move a file
4. Delete a file
5. Search the project folder for something
6. Get a list of all files in the project

