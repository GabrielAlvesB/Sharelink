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
- **Rede isolada:** só a pasta `src/Sharelink/Upload/Providers/` e o `Update/GitHubUpdateClient.cs` têm acesso à internet. O resto do app não consegue nem usar HTTP, porque o using implícito foi removido no csproj.
- **Conexões travadas:** toda requisição passa por `SecureHttp`, que só aceita HTTPS para o host exato do destino configurado, sem redirecionamentos, sem cookies e sem usuário/senha na URL. O R2 sempre usa o host fixo da Cloudflare.
- **Chaves guardadas no Windows:** chaves, Client ID e senhas de aplicativo ficam no **Gerenciador de Credenciais**, criptografadas por usuário, e nunca no `settings.json` nem no log.
- **Respostas conferidas:** os links devolvidos pelo Imgur e pelo Nextcloud são validados (https e domínio esperado), e só identificadores no formato que o próprio Sharelink gera podem ser apagados.
- **Links impossíveis de adivinhar:** cada link usa 128 bits aleatórios. O bucket não lista o conteúdo.
- **Só tipos seguros:** apenas `.png .jpg .gif .mp4` podem ser enviados, nunca HTML/SVG/scripts.
- **Integridade:** o SHA-256 do arquivo faz parte da assinatura AWS SigV4, que é validada nos testes contra o exemplo oficial da AWS.
- **Upload só quando você pede:** o app pede confirmação, que dá para desligar.
- **Sem dependências de terceiros:** nenhum pacote NuGet no app e sem telemetria. Atualizações só com assinatura digital válida.
- **Dados sensíveis:** para esconder dados num print, use **Pixelar**, que descarta a informação original.

## Instalar
Baixe o `Sharelink-Setup-X.Y.Z.exe` mais recente em [Releases](https://github.com/GabrielAlvesB/Sharelink/releases) e execute.
- Instala só para o seu usuário, sem pedir administrador, e já inclui o .NET (não precisa instalar nada antes).
- Para gravar a tela, instale também o FFmpeg: `winget install Gyan.FFmpeg`.

## Atualizações automáticas
O Sharelink verifica as releases deste repositório ao abrir e a cada 6 horas. Quando há versão nova, ele:
1. baixa o instalador e o arquivo `.sig` em segundo plano;
2. **confere a assinatura digital** (ECDSA P-256). Se não bater, apaga o arquivo;
3. avisa na galeria e na bandeja. Ao clicar em **Atualizar agora**, o app fecha, instala em modo silencioso e reabre.

A chave privada que assina as versões fica **só no PC de quem publica** (Gerenciador de Credenciais), e o app traz apenas a chave pública ([UpdatePublicKey.cs](src/Sharelink/Update/UpdatePublicKey.cs)). Mesmo com a conta do GitHub invadida, ninguém consegue publicar uma atualização que o app aceite. Dá para desligar a verificação em Configurações › Atualizações.

## Publicar uma versão nova (mantenedor)
Pré-requisitos: .NET SDK 10, [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`), GitHub CLI logado (`gh auth login`) e a chave de assinatura neste PC.

```powershell
.\scripts\release.ps1 -Version 1.1.0 -Notes "O que mudou nesta versão"
```
O script roda os testes, gera o app com o .NET embutido, cria o instalador, assina, faz commit da versão, cria a tag `v1.1.0` e publica a release.

**Chave de assinatura** (`tools/ReleaseTool`):
```powershell
dotnet run --project tools/ReleaseTool -- export-key C:\caminho\backup.key   # BACKUP: guarde num lugar seguro e apague o arquivo
dotnet run --project tools/ReleaseTool -- import-key C:\caminho\backup.key   # restaurar em outro PC
dotnet run --project tools/ReleaseTool -- check-release                       # confere a release publicada como o app faz
```
Se a chave privada for perdida, os apps instalados deixam de aceitar atualizações e será preciso reinstalar manualmente uma versão com chave nova.

## Compilar e rodar (desenvolvimento)
Pré-requisito: .NET SDK 10 (`winget install Microsoft.DotNet.SDK.10`).

```powershell
dotnet test                                    # testes automatizados
dotnet run --project src/Sharelink             # modo desenvolvimento
```

## Estrutura
```
src/Sharelink/
  TrayApplication.cs   bandeja, atalhos e fluxo das ações
  Theme/               tema claro/escuro, cores, ícones
  Controls/            componentes visuais (botões, galeria, busca, prévia)
  Gallery/             janela principal (galeria), miniaturas
  Upload/              destinos de link, credenciais, registro de links
    Providers/         rede dos destinos: S3/R2/Supabase, Imgur, WebDAV, SecureHttp
  Update/              atualização automática (GitHubUpdateClient é a única outra parte com rede)
tools/ReleaseTool/     chaves de assinatura, assinatura do instalador, ícone
installer/             script do instalador (Inno Setup)
scripts/release.ps1    publica uma versão nova
  Capture/             captura e seletor de região
  Editor/              editor de anotações
  Recording/           gravação via FFmpeg
  Output/              salvar, área de transferência, pós-captura
  Settings/            configurações
tests/Sharelink.Tests/ testes (xUnit)
```

Dados locais: `%AppData%\Sharelink\` (`settings.json`, `links.json`, `sharelink.log`). As capturas ficam em `Imagens\Sharelink\`.
