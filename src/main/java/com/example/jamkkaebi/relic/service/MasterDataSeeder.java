package com.example.jamkkaebi.relic.service;

import com.example.jamkkaebi.relic.domain.BuildingMaster;
import com.example.jamkkaebi.relic.domain.RelicMaster;
import com.example.jamkkaebi.relic.repository.BuildingMasterRepository;
import com.example.jamkkaebi.relic.repository.RelicMasterRepository;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.boot.ApplicationArguments;
import org.springframework.boot.ApplicationRunner;
import org.springframework.stereotype.Component;
import org.springframework.transaction.annotation.Transactional;

/**
 * 기동할 때 콘텐츠 마스터를 맞춘다.
 *
 * <p><b>덮어쓰기라 몇 번을 돌려도 결과는 같다</b> — 별칭이나 실화 문구를 고치면 다음 배포에 자동으로 반영되고,
 * 사용자 진행도({@code user_relics})는 유물 번호만 참조하므로 건드려지지 않는다.
 */
@Component
public class MasterDataSeeder implements ApplicationRunner {

    private static final Logger log = LoggerFactory.getLogger(MasterDataSeeder.class);

    private final RelicMasterRepository relicRepository;
    private final BuildingMasterRepository buildingRepository;

    public MasterDataSeeder(RelicMasterRepository relicRepository,
                            BuildingMasterRepository buildingRepository) {
        this.relicRepository = relicRepository;
        this.buildingRepository = buildingRepository;
    }

    @Override
    @Transactional
    public void run(ApplicationArguments args) {
        seed();
    }

    @Transactional
    public void seed() {
        int order = 0;
        for (ContentSeedData.RelicSeed seed : ContentSeedData.RELICS) {
            int displayOrder = ++order;
            relicRepository.findById(seed.id()).ifPresentOrElse(
                    existing -> existing.refresh(seed.sealMedium(), seed.poeticAlias(), seed.realName(),
                            seed.spiritDefaultName(), seed.realStory(), null, null, displayOrder),
                    () -> relicRepository.save(RelicMaster.builder()
                            .id(seed.id())
                            .era(seed.era())
                            .sealMedium(seed.sealMedium())
                            .poeticAlias(seed.poeticAlias())
                            .realName(seed.realName())
                            .spiritDefaultName(seed.spiritDefaultName())
                            .realStory(seed.realStory())
                            .displayOrder(displayOrder)
                            .build()));
        }

        for (ContentSeedData.BuildingSeed seed : ContentSeedData.BUILDINGS) {
            buildingRepository.findById(seed.id()).ifPresentOrElse(
                    existing -> existing.refresh(seed.name(), seed.realStory()),
                    () -> buildingRepository.save(BuildingMaster.builder()
                            .id(seed.id())
                            .era(seed.era())
                            .name(seed.name())
                            .realStory(seed.realStory())
                            .build()));
        }

        log.info("콘텐츠 마스터를 맞췄습니다 — 유물 {}종, 건물 {}채",
                ContentSeedData.RELICS.size(), ContentSeedData.BUILDINGS.size());
    }
}
