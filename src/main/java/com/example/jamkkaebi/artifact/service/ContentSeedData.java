package com.example.jamkkaebi.artifact.service;

import com.example.jamkkaebi.artifact.domain.Era;
import com.example.jamkkaebi.artifact.domain.SealMedium;

import java.util.List;

/**
 * 콘텐츠 마스터 원본 — 유물 9종과 건물 3채.
 * 늘지 않는 고정 데이터라 코드에 두고 {@link MasterDataSeeder}가 넣는다.
 *
 * <p><b>id는 영구 번호다.</b> 클라이언트 에셋과 도감 카드가 이 번호로 유물을 가리키므로 한 번
 * 배포한 뒤에는 바꾸지 않는다. 순서를 바꾸고 싶으면 {@code displayOrder}만 고친다.
 *
 */
final class ContentSeedData {

    private ContentSeedData() {
    }

    /**
     * @param id                영구 유물 번호
     * @param spiritDefaultName 사용자가 이름을 지어주기 전까지 쓰는 도깨비 이름
     */
    record ArtifactSeed(
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
    static final List<ArtifactSeed> ARTIFACTS = List.of(
            new ArtifactSeed(1, Era.THREE_KINGDOMS, SealMedium.RUST,
                    "고구려, 불꽃을 등진 걸음", "금동연가7년명여래입상", "불꽃걸음",
                    "1963년 경남 의령에서 한 농부가 밭일을 하다 우연히 발견했다. "
                            + "불꽃무늬 광배를 등지고 선 자세가 고구려 불상의 특징을 그대로 보여준다."),
            new ArtifactSeed(2, Era.THREE_KINGDOMS, SealMedium.SOOT,
                    "백제, 향이 스민 산맥", "금동대향로", "향산이",
                    "1993년 부여 능산리 절터 발굴 조사 중 저습지에서 출토됐다. "
                            + "산봉우리가 겹친 모양의 뚜껑을 얹은 백제 금속공예의 정수로, 국보로 지정됐다."),
            new ArtifactSeed(3, Era.THREE_KINGDOMS, SealMedium.ROOT,
                    "신라, 머리에 인 금빛 숲", "금관총 금관", "금빛숲",
                    "1921년 경주 시가지 공사 중 우연히 발견됐다. "
                            + "무덤 이름 자체가 \"금관이 나온 무덤\"으로 붙여질 만큼 상징적인 발견이었다."),
            new ArtifactSeed(4, Era.GORYEO, SealMedium.ROOT,
                    "고려, 아직 오지 않은 이", "논산 관촉사 석조미륵보살입상", "미륵이",
                    "고려를 대표하는 미륵 도상이다. 미륵은 먼 미래에 올 부처라, "
                            + "천 년 가까이 같은 자리에서 아직 오지 않은 때를 기다리고 있는 셈이다."),
            new ArtifactSeed(5, Era.GORYEO, SealMedium.SOOT,
                    "고려, 살결에 새긴 구름", "상감청자", "구름결",
                    "표면을 파낸 자리에 다른 흙을 채워 무늬를 내는 상감기법은 고려가 독자적으로 완성했다. "
                            + "청자 상감운학문 매병의 구름과 학이 그 대표다."),
            new ArtifactSeed(6, Era.GORYEO, SealMedium.FOG,
                    "고려, 바다를 두른 검은 밤", "나전칠기", "밤바다",
                    "검은 옻칠 바탕에 전복 껍데기를 얇게 갈아 박아 넣는 정교한 공예다. "
                            + "국내에 남은 고려 나전칠기는 극히 드물고 상당수가 해외에 소장돼 있다."),
            new ArtifactSeed(7, Era.JOSEON, SealMedium.RUST,
                    "조선, 붉게 남는 당부", "유서지보", "붉은당부",
                    "임금이 내리는 당부의 글에 찍던 국새다. 조선과 대한제국의 국새 다수가 "
                            + "국권 침탈기에 유실됐고, 이후 미국 등지에서 여러 점이 환수됐다."),
            new ArtifactSeed(8, Era.JOSEON, SealMedium.ROOT,
                    "조선, 밤길을 여는 말굽", "마패", "말발굽",
                    "역참의 말을 빌리는 증표이자 암행어사의 상징이었다. "
                            + "새겨진 말 그림의 개수가 곧 등급이라, 밤길에 내보이는 것만으로 신분이 증명됐다."),
            new ArtifactSeed(9, Era.JOSEON, SealMedium.FOG,
                    "조선, 참새를 올려다보는 눈", "묘작도", "묘작이",
                    "변상벽이 고양이와 참새를 함께 그린 서민 길상화다. "
                            + "국새나 마패 같은 왕실 유물과 나란히 두면 조선의 폭이 그대로 드러난다."));

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
