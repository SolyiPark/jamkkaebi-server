package com.example.jamkkaebi.relic.repository;

import com.example.jamkkaebi.relic.domain.BuildingMaster;
import com.example.jamkkaebi.relic.domain.Era;
import org.springframework.data.jpa.repository.JpaRepository;

import java.util.Optional;

public interface BuildingMasterRepository extends JpaRepository<BuildingMaster, Integer> {

    Optional<BuildingMaster> findByEra(Era era);
}
