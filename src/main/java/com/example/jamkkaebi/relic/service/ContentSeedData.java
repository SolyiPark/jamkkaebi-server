package com.example.jamkkaebi.relic.service;

import com.example.jamkkaebi.relic.domain.Era;
import com.example.jamkkaebi.relic.domain.SealMedium;

import java.util.List;

/**
 * 콘텐츠 마스터 원본 — 유물 9종과 건물 3채.
 * 늘지 않는 고정 데이터라 코드에 두고 {@link MasterDataSeeder}가 넣는다.
 *
 * <p><b>id는 영구 번호다.</b> 클라이언트 에셋과 도감 카드가 이 번호로 유물을 가리키므로 한 번
 * 배포한 뒤에는 바꾸지 않는다. 순서를 바꾸고 싶으면 {@code displayOrder}만 고친다.
 *
 * <p><b>출시 전에 한 번 바꾼 번호:</b> 1·3·4·7 은 처음에 다른 유물(금동연가7년명여래입상 · 금관총 금관 ·
 * 관촉사 석조미륵보살입상 · 유서지보)이었는데, 출시 전에 지금 유물(거북이 모양 연적 · 개구리 모양 토우 ·
 * 청자 석류 모양 연적 · 봉 뒤꽂이)로 바꿨다. 실제 플레이어가 없을 때라 기존 기록을 옮기지 않았다 —
 * 개발 DB에서 그 번호를 가진 테스트 계정은 진행도·지어 준 이름이 새 유물에 붙어 보이니, 필요하면 DB를 초기화한다.
 * <b>출시 뒤에는 이렇게 번호를 다른 유물에 다시 쓰지 않는다.</b> 유물을 빼거나 바꿔야 하면 새 번호를 주고,
 * 플레이어 기록(보유·진행도·이름·아바타·정화 판·뽑기 기록)을 옮기는 마이그레이션을 함께 만든다.
 */
final class ContentSeedData {

    private ContentSeedData() {
    }

    /**
     * @param id                영구 유물 번호
     * @param spiritDefaultName 사용자가 이름을 지어주기 전까지 쓰는 도깨비 이름
     * @param realStory         실화 3개를 줄바꿈으로 이은 것. 클라이언트는 줄 단위로 나눠 쓴다.
     *                          페르소나가 있는 도깨비(2, 5, 6, 8, 9)는
     *                          tools/npc-prompt-test/data/personas.json 의 stories 와 같은 문장이다.
     */
    record RelicSeed(
            int id,
            Era era,
            SealMedium sealMedium,
            String poeticAlias,
            String realName,
            String spiritDefaultName,
            String realStory
    ) {
    }

    record BuildingSeed(int id, Era era, String name, String realStory) {
    }

    // 같은 시대의 유물 3종은 서로 다른 매개체를 쓴다.
    static final List<RelicSeed> RELICS = List.of(
            // 출처: https://www.emuseum.go.kr/detail?relicId=PS0100100100101381300000 · https://museum.seoul.go.kr/www/relic/RelicView.do?mcsjgbnc=PS01003026001&mcseqno1=010126&mcseqno2=00000&cdLanguage=KOR
            new RelicSeed(1, Era.THREE_KINGDOMS, SealMedium.RUST,
                    "고구려, 벼루 곁에 엎드린 거북", "거북이 모양 연적", "거북이",
                    "나는 고구려 사람이 흙으로 빚은 거북 모양 연적이다\n"
                            + "연적은 벼루에 먹을 갈 때 부을 물을 담아 두는 그릇이다\n"
                            + "도자기로 만든 거북 연적 가운데 고구려 것이 가장 오래됐고, 나는 국립중앙박물관에 있다"),
            new RelicSeed(2, Era.THREE_KINGDOMS, SealMedium.SOOT,
                    "백제, 향이 스민 산맥", "금동대향로", "향산이",
                    "나는 주차장이 될 뻔한 땅을 사람들이 한 번 더 파 본 덕에 발견되어 국보가 됐다\n"
                            + "용이 연꽃 몸체를 받치고, 산봉우리 사이엔 다섯 악사, 꼭대기엔 봉황이 있다\n"
                            + "1,300년 넘게 진흙에 묻혀 있어서 옛 모습을 지킬 수 있었다"),
            // 출처: https://www.museum.go.kr/site/main/relic/search/view?relicId=6634 · https://www.seoul.co.kr/news/1998/01/20/19980120019001 · https://encykorea.aks.ac.kr/Article/E0059202
            new RelicSeed(3, Era.THREE_KINGDOMS, SealMedium.ROOT,
                    "신라, 흙에서 깨어난 개구리", "개구리 모양 토우", "개굴이",
                    "나는 신라 사람이 흙으로 빚은 개구리 모양 토우이고, 지금 국립중앙박물관에 있다\n"
                            + "신라 토우는 5~6세기 무렵 작은 돌덧널무덤에 함께 묻으려고 만들었다\n"
                            + "국보 토우장식 장경호에는 나 같은 개구리를 물고 있는 뱀이 붙어 있다"),
            // 출처: https://www.museum.go.kr/MUSEUM/contents/M0502000000.do?schM=view&searchId=search&relicId=4220 · https://www.museum.go.kr/MUSEUM/contents/M0502000000.do?schM=view&searchId=search&relicId=4251
            new RelicSeed(4, Era.GORYEO, SealMedium.ROOT,
                    "고려, 석류를 끌어안은 원숭이", "청자 석류 모양 연적", "석류동이",
                    "나는 석류를 얼싸안은 원숭이 모양 고려청자 연적이고, 원숭이 입으로 물을 따른다\n"
                            + "석류 씨는 흰 상감점을 촘촘히 찍어 나타냈다\n"
                            + "나처럼 동물과 식물 모양으로 빚은 청자는 고려청자 전성기인 12세기 전반에 많이 만들어졌다"),
            new RelicSeed(5, Era.GORYEO, SealMedium.SOOT,
                    "고려, 살결에 새긴 구름", "상감 청자 매병", "구름결",
                    "고려가 완성한 상감기법으로, 파낸 자리에 흰 백토와 붉은 자토를 채워 무늬를 냈다\n"
                            + "주꾸미가 안고 온 청자 덕에, 나 같은 고려청자를 실은 배가 태안 바다에서 발견됐다\n"
                            + "나는 지금 간송 미술관에 있다"),
            new RelicSeed(6, Era.GORYEO, SealMedium.FOG,
                    "고려, 바다를 두른 검은 밤", "나전칠기", "밤바다",
                    "옻칠 위에 전복 껍데기를 얇게 갈아 박은 공예로 만들었다\n"
                            + "국화·넝쿨 무늬로 채웠다\n"
                            + "나는 총 15점 정도만 남은 귀한 고려 나전칠기이고, 그 대부분은 외국에 흩어져 있다"),
            // 출처: https://www.emuseum.go.kr/detail?relicId=PS0100100101700660200000 · https://www.gogung.go.kr/gogung/pgm/psgudMng/view.do?psgudSn=364689 · https://news.kbs.co.kr/news/pc/view/view.do?ncd=633953
            new RelicSeed(7, Era.JOSEON, SealMedium.RUST,
                    "조선, 쪽머리에 내려앉은 봉황", "봉 뒤꽂이", "봉이",
                    "봉잠이라고도 불리는 나는 봉황 무늬로 꾸민 조선의 뒤꽂이다\n"
                            + "나를 국립중앙박물관에 기증한 김희진 선생은 평생 전통 매듭을 만든 무형문화재다\n"
                            + "국립중앙박물관은 경복궁 시절 마지막 전시로 김희진 선생이 기증한 매듭전을 열었다"),
            new RelicSeed(8, Era.JOSEON, SealMedium.ROOT,
                    "조선, 밤길을 여는 말굽", "마패", "말발굽",
                    "나는 진짜 암행어사가 지니던 마패다. 암행어사는 관리의 잘잘못과 백성의 고통을 살폈다\n"
                            + "관원의 등급에 따라 말을 새겼고, 그 수만큼 역마를 썼다\n"
                            + "나무와 철을 거쳐 구리로 만들었고, 지금 국립중앙박물관에 있다"),
            new RelicSeed(9, Era.JOSEON, SealMedium.FOG,
                    "조선, 참새를 올려다보는 눈", "묘작도", "묘작이",
                    "나를 그린 변상벽은 고양이를 잘 그려서 사람들이 그 화가를 '변고양이'라 불렀다\n"
                            + "고양이는 장수를, 참새는 기쁨을 뜻한다\n"
                            + "나는 조선 초기 한국적 영모화의 전통을 계승했다"));

    // 건물은 뽑기 대상이 아니며 조건 충족 시 자동 지급
    static final List<BuildingSeed> BUILDINGS = List.of(
            new BuildingSeed(1, Era.THREE_KINGDOMS, "석굴암",
                    "통일신라 불교 미술의 정수이자 유네스코 세계유산이다. "
                            + "일제강점기의 콘크리트 보강 공사가 오히려 결로와 습기를 불러, "
                            + "이후 여러 차례 재보수를 겪었다."),
            new BuildingSeed(2, Era.GORYEO, "경천사지 십층석탑",
                    "1348년에 세워진 대리석 석탑이다. 1907년 일본 관리가 무단 반출했으나 "
                            + "언론 보도로 국제 여론이 들끓어 1918년경 국내로 돌아왔다."),
            new BuildingSeed(3, Era.JOSEON, "경복궁",
                    "1395년 창건된 조선의 법궁이다. 임진왜란으로 불타 270여 년간 폐허로 남았다가 "
                            + "1867년 중건됐고, 일제강점기에 전각 대부분이 다시 헐린 뒤 "
                            + "지금도 복원이 진행 중이다."));
}
