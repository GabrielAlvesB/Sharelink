# Sharelink

Captura de tela pessoal no estilo do ShareX, com **galeria**, **modo escuro** e **links públicos** no destino que você escolher
(Cloudflare R2, Supabase, qualquer S3 compatível, Imgur ou Nextcloud/WebDAV). Nada sai do seu computador a não ser quando você pede "Gerar link".

## Recursos
- **Galeria** (abre ao iniciar): todas as capturas, antigas e novas, agrupadas por Hoje, Ontem e data, com busca e filtros (com link, imagens, vídeos)
- **Captura**: região (arrastar), janela (clicar nela) e tela inteira, em todos os monitores
- **Editor de anotações**: seta, retângulo, elipse, caneta, texto, marca-texto, **pixelar** e desfocar
- **Gravação de tela**: MP4 ou GIF (usa o FFmpeg)
- **Links**: "Gerar link" envia para o destino escolhido e copia a URL. "Apagar link" remove de onde foi enviado.
- **Tema**: segue o modo claro/escuro do Windows automaticamente (ou force escuro/claro em Configurações › Aparência)

## Atalhos padrão (dá para mudar em Configurações › Atalhos)
| Ação | Atalho |
|---|---|
| Capturar região / janela | Ctrl+Shift+4 |
| **Capturar e gerar link** | Ctrl+Shift+7 |
| Capturar tela inteira | Ctrl+Shift+3 |
| Capturar janela ativa | Ctrl+Shift+2 |
| Gravar MP4 / GIF (de novo para parar) | Ctrl+Shift+5 / Ctrl+Shift+6 |

**No editor:** `1`–`8` trocam de ferramenta, `Ctrl+Z`/`Ctrl+Y` desfazem e refazem, `Enter` conclui, `Ctrl+Enter` conclui e gera o link, e `Esc` descarta.

**Na galeria:** duplo clique abre, `Ctrl+C` copia, `Del` manda para a Lixeira, `Ctrl+F` busca e `F5` atualiza. Fechar a janela deixa o app na bandeja; para sair, use o botão direito no ícone › Sair.

## Destinos de link (Configurações › Links e upload)
| Destino | Onde o arquivo fica | Bom para |
|---|---|---|
| **Cloudflare R2** | Seu bucket na Cloudflare (grátis até 10 GB, sem custo de tráfego) | Recomendado |
| **Supabase Storage** | Seu projeto Supabase (grátis até 1 GB), bucket público | Opção grátis simples; usa as chaves S3 do Storage, nunca a `service_role` |
| **S3 compatível** | Amazon S3, Backblaze B2, Wasabi, DigitalOcean Spaces, MinIO no seu servidor | Quem já usa um desses |
| **Imgur** | Servidores do Imgur (terceiro) | Rapidez, sem criar conta de armazenamento; evite para prints com dados pessoais |
| **Nextcloud / WebDAV** | Seu Nextcloud (cria link público só leitura) ou qualquer WebDAV | Quem tem servidor próprio |

Cada destino tem um guia passo a passo na própria tela e um botão **Testar conexão**, que envia um arquivo minúsculo, confere o link e apaga.
Links já criados lembram onde foram enviados, então dá para apagá-los mesmo depois de trocar o destino.

## Segurança
- **Rede isolada:** só a pasta `src/Sharelink/Upload/Providers/` tem acesso à internet. O resto do app não consegue nem usar HTTP, porque o using implícito foi removido no csproj.
- **Conexões travadas:** toda requisição passa por `SecureHttp`, que só aceita HTTPS para o host exato do destino configurado, sem redirecionamentos, sem cookies e sem usuário/senha na URL. O R2 sempre usa o host fixo da Cloudflare.
- **Chaves guardadas no Windows:** chaves, Client ID e senhas de aplicativo ficam no **Gerenciador de Credenciais**, criptografadas por usuário, e nunca no `settings.json` nem no log.
- **Respostas conferidas:** os links devolvidos pelo Imgur e pelo Nextcloud são validados (https e domínio esperado), e só identificadores no formato que o próprio Sharelink gera podem ser apagados.
- **Links impossíveis de adivinhar:** cada link usa 128 bits aleatórios. O bucket não lista o conteúdo.
- **Só tipos seguros:** apenas `.png .jpg .gif .mp4` podem ser enviados, nunca HTML/SVG/scripts.
- **Integridade:** o SHA-256 do arquivo faz parte da assinatura AWS SigV4, que é validada nos testes contra o exemplo oficial da AWS.
- **Upload só quando você pede:** o app pede confirmação, que dá para desligar.
- **Sem dependências de terceiros:** nenhum pacote NuGet no app, sem telemetria e sem atualização automática.
- **Dados sensíveis:** para esconder dados num print, use **Pixelar**, que descarta a informação original.

## Compilar e rodar
Pré-requisito: .NET SDK 10 (`winget install Microsoft.DotNet.SDK.10`). Para gravar a tela: `winget install Gyan.FFmpeg`.

```powershell
dotnet test                                    # testes automatizados
dotnet run --project src/Sharelink             # modo desenvolvimento
dotnet publish src/Sharelink -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

## Estrutura
```
src/Sharelink/
  TrayApplication.cs   bandeja, atalhos e fluxo das ações
  Theme/               tema claro/escuro, cores, ícones
  Controls/            componentes visuais (botões, galeria, busca, prévia)
  Gallery/             janela principal (galeria), miniaturas
  Upload/              destinos de link, credenciais, registro de links
    Providers/         ÚNICA parte com rede: S3/R2, Imgur, WebDAV, SecureHttp
  Capture/             captura e seletor de região
  Editor/              editor de anotações
  Recording/           gravação via FFmpeg
  Output/              salvar, área de transferência, pós-captura
  Settings/            configurações
tests/Sharelink.Tests/ testes (xUnit)
```

Dados locais: `%AppData%\Sharelink\` (`settings.json`, `links.json`, `sharelink.log`). As capturas ficam em `Imagens\Sharelink\`.
