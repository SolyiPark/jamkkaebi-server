package com.example.jamkkaebi.artifact.service;

import com.example.jamkkaebi.artifact.domain.ArtifactMaster;
import com.example.jamkkaebi.artifact.domain.BuildingMaster;
import com.example.jamkkaebi.artifact.repository.ArtifactMasterRepository;
import com.example.jamkkaebi.artifact.repository.BuildingMasterRepository;
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
 * 사용자 진행도({@code user_artifacts})는 유물 번호만 참조하므로 건드려지지 않는다.
 */
@Component
public class MasterDataSeeder implements ApplicationRunner {

    private static final Logger log = LoggerFactory.getLogger(MasterDataSeeder.class);

    private final ArtifactMasterRepository artifactRepository;
    private final BuildingMasterRepository buildingRepository;

    public MasterDataSeeder(ArtifactMasterRepository artifactRepository,
                            BuildingMasterRepository buildingRepository) {
        this.artifactRepository = artifactRepository;
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
        for (ContentSeedData.ArtifactSeed seed : ContentSeedData.ARTIFACTS) {
            int displayOrder = ++order;
            artifactRepository.findById(seed.id()).ifPresentOrElse(
                    existing -> existing.refresh(seed.sealMedium(), seed.poeticAlias(), seed.realName(),
                            seed.spiritDefaultName(), seed.realStory(), null, null, displayOrder),
                    () -> artifactRepository.save(ArtifactMaster.builder()
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
                ContentSeedData.ARTIFACTS.size(), ContentSeedData.BUILDINGS.size());
    }
}
