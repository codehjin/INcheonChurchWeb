# 서버 배포 메모

오라클 서버 1대(`134.185.101.124`, Ubuntu, nginx 1.24 앞단). 평소 배포는 GitHub Actions 의
**Deploy to Docker Server** 를 수동 실행하면 재정앱과 학부모앱이 함께 올라간다 (`.github/workflows/deploy.yml`).

| | 재정앱 | 학부모 포털 |
|---|---|---|
| 주소 | `ijch-edu.kro.kr` | `ijch-school.kro.kr` |
| 서버 폴더 | `/home/ubuntu/church` (폴더째 `/app` 에 마운트) | `/home/ubuntu/church-home` (`app/`, `keys/`) |
| 컨테이너 | `church-web` (:8080) | `church-home` (`127.0.0.1:8081`) |
| compose | 서버에만 있음 | `deploy/church-home/docker-compose.yml` |
| nginx | `/etc/nginx/sites-available/church` | `deploy/nginx/ijch-school.conf` → `/etc/nginx/sites-available/ijch-school` |
| DB | `church.db` (원본) | `/home/ubuntu/church/homedata/home.db` 을 **읽기 전용**으로만 |

`home.db` 는 재정앱이 기동할 때마다 만들고(마이그레이션) `church.db` 의 공개분으로 다시 채운다.
지워지거나 깨져도 재정앱을 재시작하면 복구된다. 백업할 필요가 없다.

## 처음 한 번 — 학부모 포털 세우기 (2026-10-03 완료)

```bash
# 1) 폴더 — keys 는 컨테이너 사용자(1654)가 써야 한다
sudo mkdir -p /home/ubuntu/church-home/app /home/ubuntu/church-home/keys
sudo chown 1654:1654 /home/ubuntu/church-home/keys

# 2) 인증서 — 80 번 블록만 먼저 켜고 발급 (443 블록은 인증서 파일이 있어야 nginx -t 를 통과)
#    deploy/nginx/ijch-school.conf 의 첫 server 블록만 /etc/nginx/sites-available/ijch-school 에 넣고
sudo ln -s /etc/nginx/sites-available/ijch-school /etc/nginx/sites-enabled/ijch-school
sudo nginx -t && sudo systemctl reload nginx
sudo certbot certonly --webroot -w /var/www/html -d ijch-school.kro.kr

# 3) 파일 전체(80 + 443)로 바꾸고 다시 읽기
sudo nginx -t && sudo systemctl reload nginx
```

## 확인

```bash
curl -s http://127.0.0.1:8081/healthz            # ok — home.db 를 읽을 수 있다
sudo docker exec church-home ls / /homedata      # church.db 가 어디에도 없어야 한다
sudo docker logs church-web 2>&1 | grep 학부모    # "✅ 학부모 포털 DB: 통신문 N건 …"
```

## 주의

- 학부모 포털 화면은 **로그인 없이 열리는 곳이 `/login`·`/healthz` 뿐**이다 (Program.cs 의 FallbackPolicy).
- 학부모 계정은 재정앱 `/parent-accounts` (최고관리자)에서 만든다. 비밀번호는 만든 그 자리에서 한 번만 보인다.
- 비밀번호가 단체방 밖으로 퍼지면 **비번 재발급** 또는 **정지** — 이미 로그인한 폰도 다음 화면부터 끊긴다.
